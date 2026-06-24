namespace VibrationDashboard.Data.Entities;

/// <summary>
/// 設備 EF 實體（對映 <c>Machine</c> 資料表）。為<b>持久化模型</b>，與資料來源中立的領域模型
/// <see cref="VibrationDashboard.Models.Machine"/> 分離；<c>SqlMachineDataSource</c> 負責兩者映射。
/// 對映正規化 schema 見 docs/03 §4。狀態為衍生值，不入庫。
/// </summary>
public sealed class MachineEntity
{
    /// <summary>識別碼（VARCHAR(50) 主鍵）。</summary>
    public required string Id { get; set; }

    /// <summary>機器名稱（NVARCHAR，支援中文）。</summary>
    public required string Name { get; set; }

    /// <summary>設備類型（NVARCHAR，支援中文;選填）。</summary>
    public string? Type { get; set; }

    /// <summary>量測單位（如 mm/s）。</summary>
    public string Unit { get; set; } = "mm/s";

    /// <summary>警告門檻（黃線）。</summary>
    public double WarningThreshold { get; set; }

    /// <summary>危險門檻（紅線）。</summary>
    public double DangerThreshold { get; set; }

    /// <summary>機器圖檔（VARBINARY(MAX)）。清單查詢不必載入，明細/圖檔 API 才取。</summary>
    public byte[]? Image { get; set; }

    /// <summary>量測序列（一對多）。</summary>
    public List<MeasurementEntity> Measurements { get; set; } = [];
}
