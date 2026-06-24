namespace VibrationDashboard.Models;

/// <summary>
/// 單筆振動量測（時間 + 數值）。對應需求 3 的「10 筆含日期時間的數值」。
/// </summary>
public sealed class Measurement
{
    /// <summary>量測時間（當地時間 CST+8）。</summary>
    public DateTime Time { get; init; }

    /// <summary>量測值（單位見 <see cref="Machine.Unit"/>，如 mm/s）。</summary>
    public double Value { get; init; }

    public Measurement() { }

    public Measurement(DateTime time, double value)
    {
        Time = time;
        Value = value;
    }
}
