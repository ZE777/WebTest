using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibrationDashboard.Data.Entities;

namespace VibrationDashboard.Data.Configurations;

/// <summary>
/// <see cref="MachineEntity"/> 對映設定。欄位型別刻意對齊 docs/03 §4 的正規化 schema
/// （VARCHAR/NVARCHAR/FLOAT/VARBINARY），展示資料庫設計與型別取捨。
/// </summary>
public sealed class MachineConfiguration : IEntityTypeConfiguration<MachineEntity>
{
    public void Configure(EntityTypeBuilder<MachineEntity> b)
    {
        b.ToTable("Machine");

        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnType("varchar(50)");           // 識別碼為 ASCII，用 varchar 省空間
        b.Property(m => m.Name).HasColumnType("nvarchar(100)").IsRequired(); // 名稱含中文 → nvarchar
        b.Property(m => m.Type).HasColumnType("nvarchar(40)");               // 類型含中文 → nvarchar;選填
        b.Property(m => m.Unit).HasColumnType("varchar(20)").HasDefaultValue("mm/s");
        b.Property(m => m.WarningThreshold);                           // double → FLOAT
        b.Property(m => m.DangerThreshold);
        b.Property(m => m.Image).HasColumnType("varbinary(max)");      // 二位元圖檔

        // 一對多：刪設備時一併刪其量測（Cascade）。
        b.HasMany(m => m.Measurements)
            .WithOne(x => x.Machine!)
            .HasForeignKey(x => x.MachineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
