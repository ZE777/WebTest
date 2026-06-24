using System.Diagnostics;
using System.Text.Json;
using VibrationDashboard.Common.Exceptions;

namespace VibrationDashboard.Middleware;

/// <summary>
/// 全域例外處理:排在管線最前面,攔截所有未處理例外並統一輸出 RFC 7807 ProblemDetails。
/// <list type="bullet">
/// <item><description><see cref="AppException"/> → 用其狀態碼 / 訊息 / 錯誤碼。</description></item>
/// <item><description>其他未預期 → 500,只回 traceId,<b>不洩漏</b> stack trace(Production)。</description></item>
/// </list>
/// 對外 JSON 一律 camelCase,含 <c>errorCode</c> 與 <c>traceId</c>,對接前端錯誤處理。
/// </summary>
public sealed class ExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning(ex, "業務例外 {ErrorCode}：{Message}", ex.ErrorCode, ex.Message);
            await WriteProblemAsync(context, ex.StatusCode, ex.Message, ex.ErrorCode, ex.Details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未預期例外，路徑 {Path}", context.Request.Path);
            // Production 不洩漏細節;Development 附上例外字串便於除錯。
            var detail = _env.IsDevelopment() ? ex.ToString() : null;
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError,
                "伺服器發生未預期錯誤。", "INTERNAL_ERROR", detail);
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context, int statusCode, string title, string errorCode, object? details)
    {
        // 回應若已開始送出就無法再覆寫,直接放棄(避免 500 中又拋例外)。
        if (context.Response.HasStarted) return;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new Dictionary<string, object?>
        {
            ["status"] = statusCode,
            ["title"] = title,
            ["instance"] = context.Request.Path.Value,
            ["errorCode"] = errorCode,
            ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier
        };
        if (details is not null) problem["details"] = details;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
