using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using VibrationDashboard.Common.Settings;
using VibrationDashboard.Data;
using VibrationDashboard.DataSources;
using VibrationDashboard.DataSources.Sql;
using VibrationDashboard.Hubs;
using VibrationDashboard.Middleware;
using VibrationDashboard.Services.Machines;
using VibrationDashboard.Services.Realtime;

var builder = WebApplication.CreateBuilder(args);

// 記錄（rules/後端/02 §5）：以 Serilog 取代內建 ILogger 後端，輸出 Console + 每日輪替檔。
// 既有 ILogger<T> 呼叫（ExceptionMiddleware 業務例外→Warning、未預期→Error 含路徑；
// FileWatcher / Notifier 的資訊級流程）全部改流經 Serilog，呼叫端不需更動。
// 日誌檔寫到 ContentRoot/logs（已加入 .gitignore），保留近 14 天、每日輪替。
builder.Host.UseSerilog((context, config) => config
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// Add services to the container.
var mvc = builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(o =>
        // enum 以字串輸出（Good/Abnormal/Danger），前端易讀、不依賴數字順序。
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// 開發環境啟用 Razor 執行階段編譯：改 .cshtml 不必重建即時生效。
// （否則 View 是編譯期編進組件的，以 --no-build 或未重建時，執行中的 app 仍用舊 View，
//   可能引用不到新加入的腳本，導致前端載入中斷。Production 維持編譯期 View，效能最佳。）
if (builder.Environment.IsDevelopment())
    mvc.AddRazorRuntimeCompilation();

// Swagger / OpenAPI：開發環境提供瀏覽器測試 API 的介面。
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var xml = Path.Combine(AppContext.BaseDirectory, "VibrationDashboard.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
});

// 強型別設定繫結（MachineData 區段）。
builder.Services.Configure<MachineDataOptions>(
    builder.Configuration.GetSection(MachineDataOptions.SectionName));

// 業務服務層（薄 Controller → Service → 資料來源）。
builder.Services.AddScoped<IMachineService, MachineService>();

// 即時更新：SignalR Hub + 推播器 + 檔案監看 BackgroundService（需求 4）。
// SignalR 有獨立的 JSON 序列化器，明確設成與 REST API 一致（camelCase + enum 字串），
// 前端對 REST 與推播才能用同一套欄位存取（dto.id / dto.status="Danger"）。
builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddScoped<IMachineNotifier, MachineNotifier>();
builder.Services.AddHostedService<FileWatcherService>();

// antiforgery：讓 api.js 以 header 帶 token（寫入請求用，見 rules/前端/05）。
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

// 資料來源抽象：依設定 DataSource:Type 切換 JSON / SQL。
// 換資料來源只需改一行設定，Service / Controller / 前端皆不受影響（見 docs/02 §3）。
var dataSourceType = builder.Configuration["DataSource:Type"];
var useSql = string.Equals(dataSourceType, "Sql", StringComparison.OrdinalIgnoreCase);
if (useSql)
{
    // SQL 路徑（Phase 8，可切換）：EF Core + LocalDB。DbContext 與資料來源為 Scoped
    // （DbContext 不可單例）；唯一消費者 MachineService 亦 Scoped，生命週期相容。
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("DataSource:Type=Sql 需要 ConnectionStrings:Default。");
    builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
    builder.Services.AddScoped<IMachineDataSource, SqlMachineDataSource>();

    // 種子用：以具體型別注入 JsonFileDataSource 讀現有 JSON（不作為 IMachineDataSource）。
    builder.Services.AddSingleton<JsonFileDataSource>();
    builder.Services.AddScoped<SqlDbInitializer>();
}
else
{
    builder.Services.AddSingleton<IMachineDataSource, JsonFileDataSource>();
}

var app = builder.Build();

// SQL 路徑：啟動時套用 Migrations 並在空庫時從 JSON 種子（在 scope 內解析 Scoped 服務）。
if (useSql)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SqlDbInitializer>().InitializeAsync();
}

// HTTP 請求記錄：方法 / 路徑 / 狀態碼 / 耗時的單行摘要（Serilog.AspNetCore 內建），
// 取代逐請求手寫 log；置於管線最外層以涵蓋後續所有中介軟體的處理時間。
app.UseSerilogRequestLogging();

// 安全標頭(P0-3,見 rules/前端/05 §3)。基本標頭全環境派發;
// CSP 僅在非開發環境派發 —— 開發時 Swagger UI 與熱重載會注入內嵌腳本/樣式，嚴格 CSP 會把它們擋掉。
// 用 Response.OnStarting 在「回應送出前」套用，確保連 ExceptionMiddleware 改寫過的錯誤回應也帶標頭。
// 部署到 IIS 時亦可改由 web.config 派發(擇一,避免重複)。
var isDev = app.Environment.IsDevelopment();
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";    // 配合 P0-4:禁止瀏覽器對圖檔端點做 MIME sniffing
        h["X-Frame-Options"] = "DENY";              // 防 Clickjacking(不被嵌入 iframe)
        h["Referrer-Policy"] = "no-referrer";
        if (!isDev)
        {
            h["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; connect-src 'self' ws: wss:; " +    // connect-src 含 ws/wss → 放行 SignalR
                "frame-ancestors 'none'; base-uri 'self'; form-action 'self';";
        }
        return Task.CompletedTask;
    });
    return next();
});

// 全域例外處理:所有未處理例外統一轉 ProblemDetails(API 一致的錯誤契約)。
// 刻意「不」使用 MVC 的 app.UseExceptionHandler("/Home/Error") —— 它若註冊在此中介軟體之內層，
// 會搶先攔截端點例外並回 HTML 錯誤頁(連 404 也變 500),使本 ProblemDetails 機制在 Production 形同虛設。
// 本專案 API 優先,錯誤一律回 application/problem+json,故由此中介軟體單一負責(各環境一致)。
app.UseMiddleware<ExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

// API 屬性路由（/api/machines...）。
app.MapControllers();

// SignalR Hub:即時推播通道。
app.MapHub<MachineHub>("/hubs/machine");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

// 整合測試用:top-level statements 產生的 Program 預設為 internal,
// 補一個 public partial 讓 WebApplicationFactory<Program> 能參照進入點。
public partial class Program { }
