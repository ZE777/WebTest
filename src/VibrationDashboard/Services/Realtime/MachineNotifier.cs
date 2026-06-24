using Microsoft.AspNetCore.SignalR;
using VibrationDashboard.Hubs;
using VibrationDashboard.Services.Machines;

namespace VibrationDashboard.Services.Realtime;

/// <inheritdoc cref="IMachineNotifier"/>
public sealed class MachineNotifier : IMachineNotifier
{
    /// <summary>Hub 事件名稱:單台 delta 更新。</summary>
    public const string MachineUpdated = "MachineUpdated";

    /// <summary>Hub 事件名稱:設備移除(帶 id)。</summary>
    public const string MachineRemoved = "MachineRemoved";

    private readonly IMachineService _service;
    private readonly IHubContext<MachineHub> _hub;
    private readonly ILogger<MachineNotifier> _logger;

    public MachineNotifier(IMachineService service, IHubContext<MachineHub> hub, ILogger<MachineNotifier> logger)
    {
        _service = service;
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyChangedAsync(string id, CancellationToken ct = default)
    {
        var summary = await _service.GetSummaryAsync(id, ct);
        if (summary is null)
        {
            // 讀不到(可能正寫一半 / 鎖定):略過,等下一次事件,不誤判為移除。
            _logger.LogWarning("變更事件但讀不到設備 {Id}(可能寫入中),略過本次推播", id);
            return;
        }

        // 只推「這一台」的摘要(delta),不是全部設備。
        await _hub.Clients.All.SendAsync(MachineUpdated, summary, ct);
        _logger.LogInformation("推播 MachineUpdated:{Id} 狀態={Status} 最新值={Value}",
            id, summary.Status, summary.LatestValue);
    }

    public async Task NotifyRemovedAsync(string id, CancellationToken ct = default)
    {
        await _hub.Clients.All.SendAsync(MachineRemoved, id, ct);
        _logger.LogInformation("推播 MachineRemoved:{Id}", id);
    }
}
