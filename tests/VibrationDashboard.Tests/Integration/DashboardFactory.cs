using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace VibrationDashboard.Tests.Integration;

/// <summary>
/// 整合測試共用工廠:在記憶體啟動整個 app(TestServer),並把資料源覆寫成
/// 「JSON + 臨時種子資料夾」,讓測試資料可控、與正式 App_Data 隔離。
/// 建構時種子、Dispose 時清掉臨時夾。
/// </summary>
public sealed class DashboardFactory : WebApplicationFactory<Program>
{
    /// <summary>本工廠專用的臨時機台資料夾。</summary>
    public string Folder { get; }

    public DashboardFactory()
    {
        Folder = Path.Combine(Path.GetTempPath(), "vibdash-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Seed();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 用獨立的 "Testing" 環境:不載入開發者的 appsettings.Development.json(其 DataSource:Type 可能是 Sql),
        // 測試只吃 base appsettings.json(Json 來源)+ 下面的 InMemory 覆寫,與本機 dev 設定完全隔離。
        builder.UseEnvironment("Testing");

        // 以 InMemory 設定覆寫:強制走 JSON 來源、資料夾指到臨時夾、保留最近 10 筆。
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataSource:Type"] = "Json",
            ["MachineData:JsonFolder"] = Folder,        // 絕對路徑,JsonFileDataSource 支援
            ["MachineData:MeasurementCount"] = "10",
        }));
    }

    /// <summary>種子:良好/異常/危險各一、一台有圖、一台 15 筆量測(供 MeasurementCount 端到端案)。</summary>
    private void Seed()
    {
        WriteMachine("PUMP-A01", "泵浦 A01", 0.50, 0.88, [0.10, 0.20]);                 // 良好、無圖
        WriteMachine("FAN-B01", "風機 B01", 0.50, 0.88, [0.55, 0.60]);                  // 異常
        WriteMachine("GEAR-D01", "齒輪 D01", 0.50, 0.88, [0.90, 0.95], withImage: true);// 危險 + 有圖
        WriteMachine("LONG-1", "長序列機", 0.50, 0.88,
            [.. Enumerable.Range(0, 15).Select(i => 0.10 + i * 0.01)]);                  // 15 筆 → 測截斷
    }

    private void WriteMachine(string id, string name, double warn, double danger, double[] values, bool withImage = false)
    {
        var t0 = new DateTime(2026, 6, 1, 8, 0, 0);
        var payload = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = name,
            ["unit"] = "mm/s",
            ["warningThreshold"] = warn,
            ["dangerThreshold"] = danger,
            ["measurements"] = values.Select((v, i) => new { time = t0.AddDays(i), value = v }).ToArray(),
        };
        if (withImage)
            payload["image"] = "data:image/png;base64," + Convert.ToBase64String([1, 2, 3, 4]);

        File.WriteAllText(Path.Combine(Folder, id + ".json"), JsonSerializer.Serialize(payload));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { Directory.Delete(Folder, recursive: true); } catch { /* 清理失敗忽略 */ }
        }
    }
}
