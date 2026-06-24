using VibrationDashboard.Models;

namespace VibrationDashboard.DTOs.Responses;

/// <summary>
/// 設備摘要(看板初次載入用)。<b>不夾帶 base64 大圖</b>:圖檔改走 <see cref="ImageUrl"/> 獨立 API。
/// 含迷你趨勢 <see cref="Sparkline"/> 供卡片繪製,避免逐台再打明細 API。
/// </summary>
public sealed class MachineSummaryDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>設備類型(泵浦/風機/馬達…;無則 null),供卡片分類標籤與搜尋。</summary>
    public string? Type { get; init; }

    public required string Unit { get; init; }

    /// <summary>衍生狀態(良好/異常/危險),序列化為字串。</summary>
    public MachineStatus Status { get; init; }

    public double WarningThreshold { get; init; }
    public double DangerThreshold { get; init; }

    /// <summary>最新量測值(無資料時為 null)。</summary>
    public double? LatestValue { get; init; }

    /// <summary>最新量測時間。</summary>
    public DateTime? LatestTime { get; init; }

    /// <summary>
    /// 嚴重度 = 最新值 ÷ 危險門檻(docs/07 §2 的<b>跨機共同尺規</b>):
    /// 1.0 = 剛好到危險線、&gt;1 = 已超標。各台門檻不同，原始 mm/s 不可跨機比，
    /// 用此正規化值排「風險排行」才 actionable。無最新值或危險門檻 ≤0 時為 null。
    /// </summary>
    public double? Severity { get; init; }

    /// <summary>是否有圖檔(供前端決定要不要載圖)。</summary>
    public bool HasImage { get; init; }

    /// <summary>圖檔 API 相對網址(有圖才有值);走 ETag/304 快取。</summary>
    public string? ImageUrl { get; init; }

    /// <summary>迷你趨勢數列(僅數值,依時間遞增),供卡片 sparkline。</summary>
    public IReadOnlyList<double> Sparkline { get; init; } = [];
}
