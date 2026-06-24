using VibrationDashboard.Common.Helpers;
using VibrationDashboard.Models;

namespace VibrationDashboard.DTOs.Responses;

/// <summary>
/// 設備明細(點卡片時懶載入)。含完整量測序列供趨勢圖繪製;圖檔仍走獨立 API,不內嵌。
/// </summary>
public sealed class MachineDetailDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>設備類型(泵浦/風機/馬達…;無則 null)。</summary>
    public string? Type { get; init; }

    public required string Unit { get; init; }

    /// <summary>衍生狀態(良好/異常/危險),序列化為字串。</summary>
    public MachineStatus Status { get; init; }

    public double WarningThreshold { get; init; }
    public double DangerThreshold { get; init; }

    public bool HasImage { get; init; }

    /// <summary>圖檔 API 相對網址(有圖才有值)。</summary>
    public string? ImageUrl { get; init; }

    /// <summary>最新量測值。</summary>
    public double? LatestValue { get; init; }

    /// <summary>最新量測時間。</summary>
    public DateTime? LatestTime { get; init; }

    /// <summary>完整量測序列(依時間遞增,通常 10 筆)。</summary>
    public IReadOnlyList<MeasurementDto> Measurements { get; init; } = [];

    /// <summary>
    /// 量測序列統計摘要(平均/最大/最小/標準差/趨勢斜率/超標次數)。
    /// 後端算好給前端呈現(docs/07 §4B、§8);無量測資料時為 null。
    /// </summary>
    public MeasurementStats? Statistics { get; init; }
}
