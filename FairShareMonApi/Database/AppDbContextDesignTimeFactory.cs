using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FairShareMonApi.Database;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> / <c>migrations script</c> run OFFLINE by pinning the
/// MariaDB server version instead of auto-detecting it from a live connection (only
/// <c>database update</c> / <c>migrations list</c> need a reachable server). Bump the pinned
/// version when the target server major/minor changes (currently local MariaDB 11.7.2).
/// </summary>
public class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 7, 2)))
            .Options;

        return new AppDbContext(options);
    }
}
