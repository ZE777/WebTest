using Microsoft.AspNetCore.Http;
using VibrationDashboard.Common.Exceptions;
using VibrationDashboard.DataSources;
using VibrationDashboard.Models;
using VibrationDashboard.Services.Machines;

namespace VibrationDashboard.Tests;

/// <summary>
/// 驗證 <see cref="MachineService"/> 的業務組裝:危險優先排序、DTO 映射、
/// 無圖處理、圖檔 ETag(內容雜湊、可重現)、不存在拋 NotFound。
/// </summary>
public class MachineServiceTests
{
    private static Machine M(string id, string name, double warning, double danger, double latest, byte[]? image = null)
        => new()
        {
            Id = id,
            Name = name,
            Unit = "mm/s",
            WarningThreshold = warning,
            DangerThreshold = danger,
            ImageBytes = image,
            ImageContentType = image is null ? null : "image/png",
            Measurements =
            [
                new Measurement(new DateTime(2026, 6, 22, 8, 0, 0), latest * 0.4),
                new Measurement(new DateTime(2026, 6, 23, 8, 0, 0), latest)
            ]
        };

    private static MachineService CreateService(params Machine[] machines)
        => new(new FakeDataSource(machines));

    [Fact]
    public async Task GetSummaries_OrdersDangerFirst_AndMapsFields()
    {
        var service = CreateService(
            M("GOOD-1", "良好機", 0.5, 0.88, latest: 0.20),
            M("DNG-1", "危險機", 0.5, 0.88, latest: 0.95),
            M("ABN-1", "異常機", 0.5, 0.88, latest: 0.60));

        var list = await service.GetSummariesAsync();

        // 危險 → 異常 → 良好
        Assert.Equal(new[] { MachineStatus.Danger, MachineStatus.Abnormal, MachineStatus.Good },
            list.Select(x => x.Status).ToArray());

        var danger = list[0];
        Assert.Equal("DNG-1", danger.Id);
        Assert.Equal(0.95, danger.LatestValue);
        Assert.Equal(2, danger.Sparkline.Count); // 迷你趨勢有帶數列
    }

    [Fact]
    public async Task GetSummaries_NoImage_HasImageFalse_AndNoImageUrl()
    {
        var service = CreateService(M("NOIMG", "無圖機", 0.5, 0.88, latest: 0.2, image: null));

        var dto = (await service.GetSummariesAsync()).Single();

        Assert.False(dto.HasImage);
        Assert.Null(dto.ImageUrl);
    }

    [Fact]
    public async Task GetSummaries_WithImage_ExposesImageUrl_ButNotBytes()
    {
        var service = CreateService(M("IMG-1", "有圖機", 0.5, 0.88, latest: 0.2, image: [1, 2, 3, 4]));

        var dto = (await service.GetSummariesAsync()).Single();

        Assert.True(dto.HasImage);
        Assert.Equal("/api/machines/IMG-1/image", dto.ImageUrl);
    }

    [Fact]
    public async Task GetDetail_UnknownId_ThrowsNotFound()
    {
        var service = CreateService(M("EXISTS", "在", 0.5, 0.88, latest: 0.2));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => service.GetDetailAsync("MISSING"));
        Assert.Equal("MACHINE_NOT_FOUND", ex.ErrorCode);
        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetDetail_ReturnsMeasurements_AndStatus()
    {
        var service = CreateService(M("PUMP-A01", "冷卻水泵", 0.5, 0.88, latest: 0.63));

        var detail = await service.GetDetailAsync("PUMP-A01");

        Assert.Equal("冷卻水泵", detail.Name);
        Assert.Equal(MachineStatus.Abnormal, detail.Status);
        Assert.Equal(2, detail.Measurements.Count);
        Assert.Equal(0.63, detail.LatestValue);
    }

    [Fact]
    public async Task GetSummary_KnownId_ReturnsSummary_UnknownId_ReturnsNull()
    {
        var service = CreateService(M("PUMP-A01", "冷卻水泵", 0.5, 0.88, latest: 0.95));

        var summary = await service.GetSummaryAsync("PUMP-A01");
        Assert.NotNull(summary);
        Assert.Equal(MachineStatus.Danger, summary!.Status);   // 供即時推播組 delta 用
        Assert.Equal(0.95, summary.LatestValue);

        Assert.Null(await service.GetSummaryAsync("DOES-NOT-EXIST"));
    }

    [Fact]
    public async Task GetImage_ReturnsBytes_WithQuotedDeterministicETag()
    {
        var service = CreateService(M("IMG-1", "有圖機", 0.5, 0.88, latest: 0.2, image: [10, 20, 30, 40]));

        var img1 = await service.GetImageAsync("IMG-1");
        var img2 = await service.GetImageAsync("IMG-1");

        Assert.NotNull(img1);
        Assert.Equal("image/png", img1!.ContentType);
        Assert.StartsWith("\"", img1.ETag);
        Assert.EndsWith("\"", img1.ETag);
        Assert.Equal(img1.ETag, img2!.ETag); // 內容相同 → ETag 穩定
    }

    [Fact]
    public async Task GetImage_NoImageOrUnknown_ReturnsNull()
    {
        var service = CreateService(M("NOIMG", "無圖機", 0.5, 0.88, latest: 0.2, image: null));

        Assert.Null(await service.GetImageAsync("NOIMG"));   // 有機器但無圖
        Assert.Null(await service.GetImageAsync("MISSING")); // 機器不存在
    }

    /// <summary>記憶體版資料來源,讓 Service 可獨立測試(不依賴檔案系統)。</summary>
    private sealed class FakeDataSource : IMachineDataSource
    {
        private readonly Dictionary<string, Machine> _byId;
        public FakeDataSource(IEnumerable<Machine> machines)
            => _byId = machines.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<Machine>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Machine>>(_byId.Values.ToList());

        public Task<Machine?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_byId.GetValueOrDefault(id));
    }
}
