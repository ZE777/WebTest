using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VibrationDashboard.Services.Realtime;

namespace VibrationDashboard.Tests.Integration;

/// <summary>
/// 案 5:SignalR 即時推播的端到端整合測試。用真實 <see cref="HubConnection"/> 連記憶體
/// TestServer,從 DI 觸發 <see cref="IMachineNotifier"/>,驗證 client 真的收到 MachineUpdated,
/// 且 payload 格式(camelCase + enum 字串)與 REST 一致——這是單元測試完全碰不到的跨元件協作。
/// </summary>
public class HubTests : IClassFixture<DashboardFactory>
{
    private readonly DashboardFactory _factory;

    public HubTests(DashboardFactory factory) => _factory = factory;

    [Fact]
    public async Task NotifyChanged_PushesMachineUpdated_ToConnectedClient()
    {
        // 用 TestServer 的 handler 連 Hub(記憶體傳輸,LongPolling 對 TestServer 最穩,不需 WebSocket 接線)
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/machine", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        // 用 JsonElement 接:直接看 wire 格式,且避開 client 端沒裝字串 enum converter 的反序列化問題
        var received = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("MachineUpdated", payload => received.TrySetResult(payload));

        await connection.StartAsync();

        // 從 DI 直接觸發推播(避開 FileSystemWatcher + 去抖的計時不確定性,聚焦「Hub 序列化 + 廣播」這段)
        using (var scope = _factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IMachineNotifier>();
            await notifier.NotifyChangedAsync("FAN-B01");
        }

        // 收到推播(加逾時,免得測試卡死)
        var dto = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("FAN-B01", dto.GetProperty("id").GetString());        // payload 用 camelCase 欄位
        Assert.Equal("Abnormal", dto.GetProperty("status").GetString());   // enum 序列化為字串,與 REST 一致

        await connection.DisposeAsync();
    }
}
