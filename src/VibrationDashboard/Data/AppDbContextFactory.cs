using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace VibrationDashboard.Data;

/// <summary>
/// 設計時 DbContext 工廠：供 <c>dotnet ef migrations</c> 在不啟動主機的情況下建立 Context。
/// 從 appsettings 讀連線字串，與執行階段 <c>DataSource:Type</c> 無關（即便預設為 Json 也能產 migration）。
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少連線字串 ConnectionStrings:Default。");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
