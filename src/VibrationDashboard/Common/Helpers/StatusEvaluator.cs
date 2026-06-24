using VibrationDashboard.Models;

namespace VibrationDashboard.Common.Helpers;

/// <summary>
/// 由最新量測值對門檻判定設備狀態（對接 ISO 10816 分區）。
/// 狀態是衍生值——集中在此計算,避免散落各處造成不一致(見 docs/03 §4)。
/// </summary>
public static class StatusEvaluator
{
    /// <summary>
    /// 依最新值與門檻判定狀態(門檻採「達到即進入」語意,以對齊附圖黃/紅線):
    /// <list type="bullet">
    /// <item><description>value ≥ dangerThreshold → <see cref="MachineStatus.Danger"/></description></item>
    /// <item><description>value ≥ warningThreshold → <see cref="MachineStatus.Abnormal"/></description></item>
    /// <item><description>否則 → <see cref="MachineStatus.Good"/></description></item>
    /// </list>
    /// </summary>
    public static MachineStatus Evaluate(double latestValue, double warningThreshold, double dangerThreshold)
    {
        if (latestValue >= dangerThreshold) return MachineStatus.Danger;
        if (latestValue >= warningThreshold) return MachineStatus.Abnormal;
        return MachineStatus.Good;
    }

    /// <summary>
    /// 依設備最新一筆量測判定狀態；無量測資料時視為 <see cref="MachineStatus.Good"/>。
    /// </summary>
    public static MachineStatus Evaluate(Machine machine)
    {
        var latest = machine.Latest;
        return latest is null
            ? MachineStatus.Good
            : Evaluate(latest.Value, machine.WarningThreshold, machine.DangerThreshold);
    }
}
