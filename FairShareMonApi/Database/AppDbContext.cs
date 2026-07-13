using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database;

/// <summary>
/// Application DbContext. The model is intentionally EMPTY at this stage - no DbSets, no
/// migrations; business entities land per feature, each with its own planning doc.
/// </summary>
public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MariaDB/MySQL defaults: UTF8MB4 for full Unicode support (Vietnamese included).
        modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.UseCollation("utf8mb4_unicode_ci");

        // Per-entity mapping pattern (AppDbContext File Ownership Rule, .agents/rules/rules.md):
        // every entity ships a static ConfigureModel(ModelBuilder) in
        // Database/Entities/Partials/<Name>.cs and is invoked from here, e.g.:
        //   Member.ConfigureModel(modelBuilder);
        //   Expense.ConfigureModel(modelBuilder);

        ConfigureQueryFilters(modelBuilder);
    }

    /// <summary>Implemented in AppDbContext.partial.cs - global query filters only.</summary>
    partial void ConfigureQueryFilters(ModelBuilder modelBuilder);
}
