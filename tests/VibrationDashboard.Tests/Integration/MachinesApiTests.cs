using System.Net;
using System.Text.Json;

namespace VibrationDashboard.Tests.Integration;

/// <summary>
/// 設備 REST API 的整合測試(HTTP 端到端)。各案驗證單元測試碰不到的「對外契約」:
/// 序列化格式、排序、錯誤格式、HTTP 協定(ETag/304)、端到端設定生效。
/// </summary>
public class MachinesApiTests : IClassFixture<DashboardFactory>
{
    private readonly DashboardFactory _factory;

    public MachinesApiTests(DashboardFactory factory) => _factory = factory;

    // ── 案 1:清單 GET /api/machines ─────────────────────────────────────
    [Fact]
    public async Task GetAll_Returns200_CamelCase_EnumAsString_DangerFirst()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/machines");

        // 1) 狀態碼 + 內容型別
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);

        var json = await resp.Content.ReadAsStringAsync();

        // 2) camelCase 欄位確實出現在 wire 上(反序列化會吃掉大小寫,故直接看原始字串)
        Assert.Contains("\"latestValue\"", json);
        Assert.Contains("\"warningThreshold\"", json);
        Assert.DoesNotContain("\"LatestValue\"", json);   // 不該出現 PascalCase

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.EnumerateArray().ToList();

        // 3) 種子 4 台都回來
        Assert.Equal(4, items.Count);

        // 4) enum 序列化為字串 + 危險優先(Danger → Abnormal → 同級 id 升序的兩個 Good)
        var statuses = items.Select(x => x.GetProperty("status").GetString()!).ToArray();
        Assert.Equal(["Danger", "Abnormal", "Good", "Good"], statuses);
        Assert.Equal("GEAR-D01", items[0].GetProperty("id").GetString()); // 危險那台排最前
    }

    // ── 案 2:明細 GET /api/machines/{id} ───────────────────────────────
    [Fact]
    public async Task GetById_Existing_ReturnsDetail_WithMeasurementsAndStatistics()
    {
        var client = _factory.CreateClient();

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/machines/FAN-B01"));
        var root = doc.RootElement;

        Assert.Equal("FAN-B01", root.GetProperty("id").GetString());
        Assert.Equal("Abnormal", root.GetProperty("status").GetString());      // enum 字串
        Assert.Equal(0.60, root.GetProperty("latestValue").GetDouble(), 6);

        // 量測序列(camelCase: time / value)
        var measurements = root.GetProperty("measurements");
        Assert.Equal(2, measurements.GetArrayLength());
        Assert.True(measurements[0].TryGetProperty("time", out _));
        Assert.True(measurements[0].TryGetProperty("value", out _));

        // 後端統計(MeasurementAnalyzer 算好)——有量測時為物件(非 null),欄位齊
        var stats = root.GetProperty("statistics");
        Assert.Equal(JsonValueKind.Object, stats.ValueKind);                   // 非 null
        Assert.Equal(2, stats.GetProperty("count").GetInt32());
        Assert.Equal(0.575, stats.GetProperty("average").GetDouble(), 3);      // (0.55+0.60)/2
        Assert.True(stats.GetProperty("slopePerDay").GetDouble() > 0);         // 0.55→0.60 上升
        Assert.True(stats.TryGetProperty("max", out _));
        Assert.True(stats.TryGetProperty("stdDev", out _));
        Assert.True(stats.TryGetProperty("exceedWarning", out _));
    }

    // ── 案 3:404 + ProblemDetails GET /api/machines/{未知} ─────────────
    [Fact]
    public async Task GetById_Unknown_Returns404_AsProblemDetails()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/machines/NOPE-X01");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType); // RFC7807 媒體型別

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal("MACHINE_NOT_FOUND", root.GetProperty("errorCode").GetString());     // 領域例外的錯誤碼
        Assert.False(string.IsNullOrEmpty(root.GetProperty("traceId").GetString()));       // 可回頭對 log
        Assert.Contains("NOPE-X01", root.GetProperty("title").GetString());               // 訊息含 key
    }

    // ── 案 4:圖檔 ETag / 304 GET /api/machines/{id}/image ──────────────
    [Fact]
    public async Task GetImage_ReturnsETag_ThenConditionalRequest_Returns304()
    {
        var client = _factory.CreateClient();

        // 第一次:200 + image/png + ETag + 有內容
        var first = await client.GetAsync("/api/machines/GEAR-D01/image");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        var etag = first.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrEmpty(etag));
        Assert.True((await first.Content.ReadAsByteArrayAsync()).Length > 0);

        // 第二次:帶 If-None-Match 回剛拿到的 ETag → 304 + 空 body(不重傳內容)
        var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/machines/GEAR-D01/image");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);   // 原樣帶回(含引號),對齊 Controller 字串比對
        var second = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(0, (await second.Content.ReadAsByteArrayAsync()).Length);
    }

    // ── 案 4b:無圖的設備請求圖檔 → 404 ────────────────────────────────
    [Fact]
    public async Task GetImage_MachineWithoutImage_Returns404()
    {
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/machines/PUMP-A01/image");   // PUMP-A01 種子無圖

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── 案 6:MeasurementCount 端到端(回歸護欄) ───────────────────────
    [Fact]
    public async Task MeasurementCount_LimitsDetailToLatestN_EndToEnd()
    {
        var client = _factory.CreateClient();

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/machines/LONG-1"));
        var measurements = doc.RootElement.GetProperty("measurements");

        // 種子 15 筆(value = 0.10 + i*0.01,i=0..14)+ 設定保留 10 筆 → 只回最近 10 筆
        Assert.Equal(10, measurements.GetArrayLength());
        // 保留的是「最新端」(i=5..14):首筆 0.15(非最舊 0.10)、末筆 0.24(最新),且仍依時間遞增
        Assert.Equal(0.15, measurements[0].GetProperty("value").GetDouble(), 6);
        Assert.Equal(0.24, measurements[9].GetProperty("value").GetDouble(), 6);
    }
}
