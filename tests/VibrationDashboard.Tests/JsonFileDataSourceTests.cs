using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VibrationDashboard.Common.Helpers;
using VibrationDashboard.Common.Settings;
using VibrationDashboard.DataSources;
using VibrationDashboard.DataSources.Json;
using VibrationDashboard.Models;

namespace VibrationDashboard.Tests;

/// <summary>
/// 驗證 JSON 資料來源:讀資料夾回傳機器清單、狀態三態涵蓋、損毀檔略過、
/// 依 id 取單台、路徑穿越防護、圖檔 data URI 解析為二位元。
/// </summary>
public sealed class JsonFileDataSourceTests : IDisposable
{
    private readonly string _folder;

    public JsonFileDataSourceTests()
    {
        // 每個測試實例獨立的暫存資料夾(避免互相干擾)。
        _folder = Path.Combine(Path.GetTempPath(), "vibdash-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* 清理失敗忽略 */ }
    }

    private JsonFileDataSource CreateDataSource(int measurementCount = 10)
    {
        var options = Options.Create(new MachineDataOptions { JsonFolder = _folder, MeasurementCount = measurementCount });
        var env = new TestHostEnvironment { ContentRootPath = _folder };
        return new JsonFileDataSource(options, env, NullLogger<JsonFileDataSource>.Instance);
    }

    /// <summary>寫入一台含 <paramref name="count"/> 筆每日遞增量測的設備檔(最後一筆值最大)。</summary>
    private void WriteMachineFileWithSeries(string id, int count)
    {
        var measurements = Enumerable.Range(0, count)
            .Select(i => new MeasurementFileModel
            {
                Time = new DateTime(2026, 1, 1, 8, 0, 0).AddDays(i),
                Value = 0.10 + i * 0.01
            })
            .ToList();
        var model = new MachineFileModel
        {
            Id = id,
            Name = id,
            Unit = "mm/s",
            WarningThreshold = 0.50,
            DangerThreshold = 0.88,
            Measurements = measurements
        };
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(_folder, id + ".json"), json);
    }

    private void WriteMachineFile(string id, string name, double warning, double danger, double latestValue, string? image = null)
    {
        var model = new MachineFileModel
        {
            Id = id,
            Name = name,
            Unit = "mm/s",
            WarningThreshold = warning,
            DangerThreshold = danger,
            Image = image,
            Measurements =
            [
                new MeasurementFileModel { Time = new DateTime(2026, 6, 22, 8, 0, 0), Value = latestValue * 0.5 },
                new MeasurementFileModel { Time = new DateTime(2026, 6, 23, 8, 0, 0), Value = latestValue }
            ]
        };
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(_folder, id + ".json"), json);
    }

    [Fact]
    public async Task GetAllAsync_ReadsValidFiles_SkipsCorrupt_AndCoversAllStatuses()
    {
        WriteMachineFile("GOOD-1", "良好機", 0.50, 0.88, latestValue: 0.20);
        WriteMachineFile("ABN-1", "異常機", 0.50, 0.88, latestValue: 0.60);
        WriteMachineFile("DNG-1", "危險機", 0.50, 0.88, latestValue: 0.95);
        // 損毀檔:模擬讀到寫一半的 JSON,應被略過而非拋例外。
        File.WriteAllText(Path.Combine(_folder, "BROKEN.json"), "{ \"id\": \"BROKEN\", ");

        var sut = CreateDataSource();
        var machines = await sut.GetAllAsync();

        Assert.Equal(3, machines.Count); // 損毀檔被略過

        var statuses = machines.Select(StatusEvaluator.Evaluate).ToHashSet();
        Assert.Contains(MachineStatus.Good, statuses);
        Assert.Contains(MachineStatus.Abnormal, statuses);
        Assert.Contains(MachineStatus.Danger, statuses);
    }

    [Fact]
    public async Task GetAllAsync_EmptyOrMissingFolder_ReturnsEmpty()
    {
        var sut = CreateDataSource(); // 資料夾存在但無檔
        var machines = await sut.GetAllAsync();
        Assert.Empty(machines);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsMachine_WithSortedMeasurements()
    {
        WriteMachineFile("PUMP-A01", "冷卻水泵 A01", 0.50, 0.88, latestValue: 0.63);

        var sut = CreateDataSource();
        var machine = await sut.GetByIdAsync("PUMP-A01");

        Assert.NotNull(machine);
        Assert.Equal("PUMP-A01", machine!.Id);
        Assert.Equal("冷卻水泵 A01", machine.Name);
        Assert.Equal(2, machine.Measurements.Count);
        // 量測依時間遞增排序,Latest 取最後一筆。
        Assert.NotNull(machine.Latest);
        Assert.Equal(0.63, machine.Latest!.Value, precision: 6);
        Assert.Equal(MachineStatus.Abnormal, StatusEvaluator.Evaluate(machine));
    }

    [Fact]
    public async Task GetByIdAsync_KeepsOnlyLatestNMeasurements_WhenSeriesExceedsCount()
    {
        // 寫入 15 筆,設定只保留最近 10 筆 → 應截斷為 10 筆,且保留的是最新端(最後一筆 = 全序列最大值)。
        WriteMachineFileWithSeries("LONG-1", count: 15);

        var sut = CreateDataSource(measurementCount: 10);
        var machine = await sut.GetByIdAsync("LONG-1");

        Assert.NotNull(machine);
        Assert.Equal(10, machine!.Measurements.Count);                 // 截斷生效
        Assert.Equal(0.10 + 14 * 0.01, machine.Latest!.Value, precision: 6); // 最新端保留(第 15 筆)
        // 截斷後仍依時間遞增:第一筆應為第 6 筆原始資料(index 5),非最舊的 index 0。
        Assert.Equal(new DateTime(2026, 1, 1, 8, 0, 0).AddDays(5), machine.Measurements[0].Time);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var sut = CreateDataSource();
        Assert.Null(await sut.GetByIdAsync("DOES-NOT-EXIST"));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task GetByIdAsync_InvalidOrTraversalId_ReturnsNull(string id)
    {
        var sut = CreateDataSource();
        Assert.Null(await sut.GetByIdAsync(id));
    }

    [Fact]
    public async Task GetByIdAsync_ParsesImageDataUri_ToBytes()
    {
        var imageBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(imageBytes);
        WriteMachineFile("IMG-1", "有圖機", 0.50, 0.88, latestValue: 0.10, image: dataUri);

        var sut = CreateDataSource();
        var machine = await sut.GetByIdAsync("IMG-1");

        Assert.NotNull(machine);
        Assert.Equal("image/png", machine!.ImageContentType);
        Assert.Equal(imageBytes, machine.ImageBytes);
    }
}

/// <summary>測試用 <see cref="IHostEnvironment"/> 替身。</summary>
internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string ApplicationName { get; set; } = "Tests";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
