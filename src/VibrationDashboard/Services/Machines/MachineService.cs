using System.Security.Cryptography;
using VibrationDashboard.Common.Exceptions;
using VibrationDashboard.Common.Helpers;
using VibrationDashboard.DataSources;
using VibrationDashboard.DTOs.Responses;
using VibrationDashboard.Models;

namespace VibrationDashboard.Services.Machines;

/// <inheritdoc cref="IMachineService"/>
public sealed class MachineService : IMachineService
{
    private readonly IMachineDataSource _dataSource;

    public MachineService(IMachineDataSource dataSource) => _dataSource = dataSource;

    public async Task<IReadOnlyList<MachineSummaryDto>> GetSummariesAsync(CancellationToken ct = default)
    {
        var machines = await _dataSource.GetAllAsync(ct);
        return machines
            .Select(ToSummary)
            .OrderByDescending(m => m.Status) // enum:Danger(2) > Abnormal(1) > Good(0) → 危險優先
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MachineSummaryDto?> GetSummaryAsync(string id, CancellationToken ct = default)
    {
        var machine = await _dataSource.GetByIdAsync(id, ct);
        return machine is null ? null : ToSummary(machine);
    }

    public async Task<MachineDetailDto> GetDetailAsync(string id, CancellationToken ct = default)
    {
        var machine = await _dataSource.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Machine", id);
        return ToDetail(machine);
    }

    public async Task<MachineImage?> GetImageAsync(string id, CancellationToken ct = default)
    {
        var machine = await _dataSource.GetByIdAsync(id, ct);
        if (machine?.ImageBytes is not { Length: > 0 } bytes) return null;

        var etag = ComputeETag(bytes);
        return new MachineImage(bytes, machine.ImageContentType ?? "application/octet-stream", etag);
    }

    private static MachineSummaryDto ToSummary(Machine m)
    {
        var latest = m.Latest;
        var hasImage = m.ImageBytes is { Length: > 0 };
        return new MachineSummaryDto
        {
            Id = m.Id,
            Name = m.Name,
            Type = m.Type,
            Unit = m.Unit,
            Status = StatusEvaluator.Evaluate(m),
            WarningThreshold = m.WarningThreshold,
            DangerThreshold = m.DangerThreshold,
            LatestValue = latest?.Value,
            LatestTime = latest?.Time,
            Severity = ComputeSeverity(latest?.Value, m.DangerThreshold),
            HasImage = hasImage,
            ImageUrl = hasImage ? ImageUrlFor(m.Id, m.ImageBytes!) : null,
            Sparkline = m.Measurements.Select(x => x.Value).ToList()
        };
    }

    /// <summary>嚴重度 = 最新值 ÷ 危險門檻(跨機共同尺規);無最新值或門檻 ≤0 時為 null。</summary>
    private static double? ComputeSeverity(double? latestValue, double dangerThreshold)
        => latestValue is { } v && dangerThreshold > 0
            ? Math.Round(v / dangerThreshold, 4, MidpointRounding.AwayFromZero)
            : null;

    private static MachineDetailDto ToDetail(Machine m)
    {
        var latest = m.Latest;
        var hasImage = m.ImageBytes is { Length: > 0 };
        return new MachineDetailDto
        {
            Id = m.Id,
            Name = m.Name,
            Type = m.Type,
            Unit = m.Unit,
            Status = StatusEvaluator.Evaluate(m),
            WarningThreshold = m.WarningThreshold,
            DangerThreshold = m.DangerThreshold,
            HasImage = hasImage,
            ImageUrl = hasImage ? ImageUrlFor(m.Id, m.ImageBytes!) : null,
            LatestValue = latest?.Value,
            LatestTime = latest?.Time,
            Measurements = m.Measurements.Select(x => new MeasurementDto(x.Time, x.Value)).ToList(),
            Statistics = MeasurementAnalyzer.Analyze(m.Measurements, m.WarningThreshold)
        };
    }

    // 圖片 URL 附「內容版本指紋」（?v=雜湊）：圖檔內容變了 URL 才變 → 前端 patch 比對 src 不同就換 src、
    // 觸發瀏覽器重抓;內容沒變則 URL 不變、不會每次量測更新都白抓。版本取雜湊前 4 位元組（8 hex）夠用。
    private static string ImageUrlFor(string id, byte[] imageBytes)
        => $"/api/machines/{id}/image?v={Convert.ToHexString(SHA256.HashData(imageBytes).AsSpan(0, 4))}";

    /// <summary>以內容雜湊產生強 ETag(內容改變才會變),取 SHA-256 前 16 位元組。</summary>
    private static string ComputeETag(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return $"\"{Convert.ToHexString(hash.AsSpan(0, 16))}\"";
    }
}
