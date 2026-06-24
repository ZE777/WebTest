namespace VibrationDashboard.Common.Exceptions;

/// <summary>
/// 業務例外基底:帶 HTTP 狀態碼、錯誤碼與選用細節。
/// 繼承它的例外視為「可預期的業務錯誤」,由 <c>ExceptionMiddleware</c> 轉成對應狀態碼的 ProblemDetails;
/// 未繼承者視為非預期 → 500(不洩漏細節)。
/// </summary>
public class AppException : Exception
{
    /// <summary>對外 HTTP 狀態碼。</summary>
    public int StatusCode { get; }

    /// <summary>機器可讀錯誤碼,命名 <c>{RESOURCE}_{ACTION}</c>,如 <c>MACHINE_NOT_FOUND</c>。</summary>
    public string ErrorCode { get; }

    /// <summary>選用的額外細節(如驗證錯誤欄位)。</summary>
    public object? Details { get; }

    public AppException(string message, int statusCode, string errorCode, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }
}

/// <summary>資源不存在(404)。</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string resource, object key)
        : base($"{resource} 不存在 (key={key})",
               StatusCodes.Status404NotFound,
               $"{resource.ToUpperInvariant()}_NOT_FOUND")
    {
    }
}
