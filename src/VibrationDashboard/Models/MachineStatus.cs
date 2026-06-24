namespace VibrationDashboard.Models;

/// <summary>
/// 設備健康狀態（對接 ISO 10816 分區）。狀態為<b>衍生值</b>，
/// 由「最新量測值 vs 門檻」即時判定（見 <c>StatusEvaluator</c>），不存死值以避免資料不一致。
/// </summary>
public enum MachineStatus
{
    /// <summary>良好（綠，Zone A/B）：最新值低於警告門檻。</summary>
    Good,

    /// <summary>異常（琥珀，Zone C）：最新值介於警告與危險門檻之間。</summary>
    Abnormal,

    /// <summary>危險（紅，Zone D）：最新值達到或超過危險門檻。</summary>
    Danger
}
