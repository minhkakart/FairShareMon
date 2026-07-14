using FairShareMonApi.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database;

/// <summary>
/// Application DbContext. Business entities land per feature, each with its own planning doc;
/// every entity ships a static <c>ConfigureModel(ModelBuilder)</c> in
/// <c>Database/Entities/Partials/&lt;Name&gt;.cs</c> invoked from <see cref="OnModelCreating"/>
/// (AppDbContext File Ownership Rule, .agents/rules/rules.md).
/// </summary>
public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<Share> Shares => Set<Share>();

    public DbSet<ExpenseTag> ExpenseTags => Set<ExpenseTag>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<TierGrant> TierGrants => Set<TierGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MariaDB/MySQL defaults: UTF8MB4 for full Unicode support (Vietnamese included).
        modelBuilder.HasCharSet("utf8mb4");
        modelBuilder.UseCollation("utf8mb4_unicode_ci");

        User.ConfigureModel(modelBuilder);
        AuthToken.ConfigureModel(modelBuilder);
        Member.ConfigureModel(modelBuilder);
        Category.ConfigureModel(modelBuilder);
        Tag.ConfigureModel(modelBuilder);
        Event.ConfigureModel(modelBuilder);
        Expense.ConfigureModel(modelBuilder);
        Share.ConfigureModel(modelBuilder);
        ExpenseTag.ConfigureModel(modelBuilder);
        AuditLog.ConfigureModel(modelBuilder);
        BankAccount.ConfigureModel(modelBuilder);
        TierGrant.ConfigureModel(modelBuilder);

        ConfigureQueryFilters(modelBuilder);
    }

    /// <summary>Implemented in AppDbContext.partial.cs - global query filters only.</summary>
    partial void ConfigureQueryFilters(ModelBuilder modelBuilder);
}
