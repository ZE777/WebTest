using Microsoft.EntityFrameworkCore;
using VibrationDashboard.Data.Entities;

namespace VibrationDashboard.Data;

/// <summary>
/// SQL 路徑的 EF Core 內容（Code-first）。僅在 <c>DataSource:Type=Sql</c> 時註冊。
/// schema 由本 Context + <c>Configurations/</c> 定義，經 Migrations 建立（見 rules/後端/01 §5）。
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MachineEntity> Machines => Set<MachineEntity>();
    public DbSet<MeasurementEntity> Measurements => Set<MeasurementEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 套用本組件內所有 IEntityTypeConfiguration（MachineConfiguration、MeasurementConfiguration）。
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
