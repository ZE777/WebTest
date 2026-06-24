namespace VibrationDashboard.Services.Realtime;

/// <summary>
/// 設備變更的即時推播器。把「重讀設備 + 組 delta + 透過 Hub 推播」收斂在此,
/// 讓 <c>FileWatcherService</c> 與 <c>MachineService</c> 都不直接耦合 SignalR 細節。
/// </summary>
public interface IMachineNotifier
{
    /// <summary>設備檔新增/變更:重讀該台、組摘要 delta、推 <c>MachineUpdated</c>。讀不到(可能寫一半)則略過。</summary>
    Task NotifyChangedAsync(string id, CancellationToken ct = default);

    /// <summary>設備檔刪除:推 <c>MachineRemoved</c>(只帶 id)。</summary>
    Task NotifyRemovedAsync(string id, CancellationToken ct = default);
}
