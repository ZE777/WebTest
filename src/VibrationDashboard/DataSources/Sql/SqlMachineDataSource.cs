using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibrationDashboard.Common.Settings;
using VibrationDashboard.Data;
using VibrationDashboard.Data.Entities;
using VibrationDashboard.Models;

namespace VibrationDashboard.DataSources.Sql;

/// <summary>
/// 可切換的 MS-SQL 資料來源（EF Core）。實作與 <see cref="JsonFileDataSource"/> 同一支
/// <see cref="IMachineDataSource"/> 介面，故切換 <c>DataSource:Type</c> 後 Service/Controller/前端皆不需改。
/// 把 EF 持久化實體映射為資料來源中立的 <see cref="Machine"/>；狀態仍由上層 <c>StatusEvaluator</c> 衍生。
/// </summary>
public sealed class SqlMachineDataSource : IMachineDataSource
{
    private readonly AppDbContext _db;
    private readonly int _measurementCount;

    public SqlMachineDataSource(AppDbContext db, IOptions<MachineDataOptions> options)
    {
        _db = db;
        // ≤0 視為不限制（取全部）；否則只取最近 N 筆。
        _measurementCount = options.Value.MeasurementCount > 0 ? options.Value.MeasurementCount : int.MaxValue;
    }

    public async Task<IReadOnlyList<Machine>> GetAllAsync(CancellationToken ct = default)
    {
        // AsNoTracking：唯讀查詢免快照，較省；AsSplitQuery：避免一對多 Include 造成列數相乘。
        // 篩選式 Include：每台只撈最近 N 筆（降序取 N），走 (MachineId, MeasuredAt) 索引避免全量載入；
        // MapToMachine 再依時間遞增排回，與 JSON 來源一致。
        var entities = await _db.Machines
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.Measurements.OrderByDescending(x => x.MeasuredAt).Take(_measurementCount))
            .ToListAsync(ct);

        return entities.Select(MapToMachine).ToList();
    }

    public async Task<Machine?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        // 參數化查詢，無注入/路徑穿越風險；查無資料回 null（上層轉 404）。
        var entity = await _db.Machines
            .AsNoTracking()
            .Include(m => m.Measurements.OrderByDescending(x => x.MeasuredAt).Take(_measurementCount))
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return entity is null ? null : MapToMachine(entity);
    }

    private static Machine MapToMachine(MachineEntity e)
    {
        // 量測依時間遞增排序，讓 Machine.Latest 取最後一筆即為最新（與 JSON 來源一致）。
        var measurements = e.Measurements
            .OrderBy(m => m.MeasuredAt)
            .Select(m => new Measurement(m.MeasuredAt, m.Value))
            .ToList();

        var hasImage = e.Image is { Length: > 0 };

        return new Machine
        {
            Id = e.Id,
            Name = e.Name,
            Type = e.Type,
            Unit = e.Unit,
            WarningThreshold = e.WarningThreshold,
            DangerThreshold = e.DangerThreshold,
            ImageBytes = e.Image,
            // schema 只存二位元（docs/03 §4），不存 MIME；本專案圖檔皆 PNG，故固定回 image/png。
            ImageContentType = hasImage ? "image/png" : null,
            Measurements = measurements
        };
    }
}
