using Microsoft.AspNetCore.SignalR;

namespace VibrationDashboard.Hubs;

/// <summary>
/// 設備即時推播 Hub。本專案前端→後端不需呼叫方法,Hub 主要當<b>推播通道</b>:
/// 由 <c>MachineNotifier</c> 透過 <see cref="IHubContext{MachineHub}"/> 推
/// <c>MachineUpdated</c>(單台 delta)/ <c>MachineRemoved</c>(id)給所有連線。
/// </summary>
public sealed class MachineHub : Hub
{
}
