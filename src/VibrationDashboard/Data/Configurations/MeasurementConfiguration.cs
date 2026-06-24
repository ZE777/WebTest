using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibrationDashboard.Data.Entities;

namespace VibrationDashboard.Data.Configurations;

/// <summary>
/// <see cref="MeasurementEntity"/> 對映設定。重點是 (MachineId, MeasuredAt DESC) 複合索引，
/// 讓「取某台最近 N 筆」走索引很快（見 docs/03 §4）。
/// </summary>
public sealed class MeasurementConfiguration : IEntityTypeConfiguration<MeasurementEntity>
{
    public void Configure(EntityTypeBuilder<MeasurementEntity> b)
    {
        b.ToTable("Measurement");

        b.HasKey(x => x.Id);                                          // BIGINT IDENTITY（EF 預設代理鍵）
        b.Property(x => x.MachineId).HasColumnType("varchar(50)").IsRequired();
        b.Property(x => x.MeasuredAt).HasColumnType("datetime2");
        b.Property(x => x.Value);                                     // double → FLOAT

        // 取最近 N 筆的關鍵索引：依機器分群、時間遞減。
        b.HasIndex(x => new { x.MachineId, x.MeasuredAt })
            .HasDatabaseName("IX_Measurement_Machine_Time")
            .IsDescending(false, true);
    }
}
