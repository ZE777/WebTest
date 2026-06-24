using Microsoft.EntityFrameworkCore;
using VibrationDashboard.Data.Entities;
using VibrationDashboard.DataSources;

namespace VibrationDashboard.Data;

/// <summary>
/// SQL 路徑啟動初始化：套用 Migrations 建庫，並在<b>空庫時</b>從現有 JSON 檔種子資料。
/// 種子來源沿用已測過的 <see cref="JsonFileDataSource"/>，達成「同一份資料、兩種後端、相同畫面」，
/// 也讓「切設定即可改用 SQL」開箱即用（驗收條件）。
/// </summary>
public sealed class SqlDbInitializer
{
    private readonly AppDbContext _db;
    private readonly JsonFileDataSource _jsonSeedSource;
    private readonly ILogger<SqlDbInitializer> _logger;

    public SqlDbInitializer(
        AppDbContext db,
        JsonFileDataSource jsonSeedSource,
        ILogger<SqlDbInitializer> logger)
    {
        _db = db;
        _jsonSeedSource = jsonSeedSource;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Code-first：套用 Migrations（DB 不存在會一併建立）。
        await _db.Database.MigrateAsync(ct);

        if (await _db.Machines.AnyAsync(ct))
        {
            _logger.LogInformation("SQL 資料庫已有資料，略過種子。");
            return;
        }

        var machines = await _jsonSeedSource.GetAllAsync(ct);
        if (machines.Count == 0)
        {
            _logger.LogWarning("找不到可種子的 JSON 資料，SQL 庫保持空白。");
            return;
        }

        var entities = machines.Select(m => new MachineEntity
        {
            Id = m.Id,
            Name = m.Name,
            Type = m.Type,
            Unit = m.Unit,
            WarningThreshold = m.WarningThreshold,
            DangerThreshold = m.DangerThreshold,
            Image = m.ImageBytes,
            Measurements = m.Measurements
                .Select(x => new MeasurementEntity
                {
                    MachineId = m.Id,
                    MeasuredAt = x.Time,
                    Value = x.Value
                })
                .ToList()
        });

        _db.Machines.AddRange(entities);
        var rows = await _db.SaveChangesAsync(ct);
        _logger.LogInformation("SQL 種子完成：{Machines} 台、共寫入 {Rows} 列。", machines.Count, rows);
    }
}
