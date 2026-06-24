namespace VibrationDashboard.Data.Entities;

/// <summary>
/// 單筆量測 EF 實體（對映 <c>Measurement</c> 資料表）。一對多隸屬 <see cref="MachineEntity"/>，
/// 以外鍵 <see cref="MachineId"/> 參照設備主檔，保證參照完整性（見 docs/03 §4）。
/// </summary>
public sealed class MeasurementEntity
{
    /// <summary>代理主鍵（BIGINT IDENTITY）。</summary>
    public long Id { get; set; }

    /// <summary>所屬設備識別碼（外鍵 → Machine.Id）。</summary>
    public required string MachineId { get; set; }

    /// <summary>量測時間（DATETIME2）。</summary>
    public DateTime MeasuredAt { get; set; }

    /// <summary>量測值。</summary>
    public double Value { get; set; }

    /// <summary>導覽屬性（回指所屬設備）。</summary>
    public MachineEntity? Machine { get; set; }
}
