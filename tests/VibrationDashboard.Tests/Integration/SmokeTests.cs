using System.Net;

namespace VibrationDashboard.Tests.Integration;

/// <summary>
/// Step 0 smoke:證明整個 app 能在記憶體 TestServer 啟動,且請求走完整管線回 200。
/// 這案綠燈代表「整合測試鷹架就緒」,後續各案才有意義。
/// </summary>
public class SmokeTests : IClassFixture<DashboardFactory>
{
    private readonly DashboardFactory _factory;

    public SmokeTests(DashboardFactory factory) => _factory = factory;

    [Fact]
    public async Task App_Boots_AndServesMachinesEndpoint()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/machines");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
