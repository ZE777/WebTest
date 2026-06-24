namespace VibrationDashboard.Common.Settings;

/// <summary>
/// JSON 資料來源與門檻預設設定，繫結 appsettings 的 <c>MachineData</c> 區段。
/// 以強型別 Options 注入，避免散落各處讀 Configuration 字串。
/// </summary>
public sealed class MachineDataOptions
{
    public const string SectionName = "MachineData";

    /// <summary>監看 / 讀取的 JSON 資料夾（相對 ContentRoot；亦接受絕對路徑）。</summary>
    public string JsonFolder { get; set; } = "App_Data/machines";

    /// <summary>單位預設值（JSON 未指定時採用）。</summary>
    public string DefaultUnit { get; set; } = "mm/s";

    /// <summary>警告門檻預設值（JSON 未指定時採用）。</summary>
    public double DefaultWarningThreshold { get; set; } = 0.5;

    /// <summary>危險門檻預設值（JSON 未指定時採用）。</summary>
    public double DefaultDangerThreshold { get; set; } = 0.88;

    /// <summary>每台保留 / 顯示的量測筆數（取最近 N 筆）。預設 40，對應 Seed 產生的 40 天每日資料。</summary>
    public int MeasurementCount { get; set; } = 40;
}
