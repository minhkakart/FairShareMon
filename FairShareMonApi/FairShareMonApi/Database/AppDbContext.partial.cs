using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database;

/// <summary>
/// Reserved for global query filters (<c>HasQueryFilter</c>) ONLY - no entity model declaration
/// blocks in this file (AppDbContext File Ownership Rule, .agents/rules/rules.md).
/// </summary>
public partial class AppDbContext
{
    partial void ConfigureQueryFilters(ModelBuilder modelBuilder)
    {
        // Global query filters (soft-delete, ownership) will be added here by feature work.
    }
}
