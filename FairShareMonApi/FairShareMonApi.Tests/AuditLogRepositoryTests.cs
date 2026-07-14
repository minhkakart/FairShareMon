using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>AuditLogRepository</c> against the real MariaDB (skippable). Covers the
/// per-expense history read (OQ17): scoped by <c>actor_user_id</c> + <c>expense_uuid</c> (NOT by the
/// possibly-deleted expense row), time-ordered ascending, empty for a foreign/unknown uuid (leaks
/// nothing), and still readable after the expense is hard-deleted (§3.8) - proving the plain, no-FK
/// <c>expense_uuid</c> survives the delete.
/// </summary>
[Collection("AuthIntegration")]
public class AuditLogRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CreateExpenseData CreateData(string name = "Ăn trưa") =>
        new(name, null, Noon, null, null, [], []);

    [SkippableFact]
    public async Task ListByExpenseAsync_ReturnsCreateRowsForTheExpenseTimeOrdered()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());

        var history = await CreateAuditLogRepository().ListByExpenseAsync(ledger.User.Uuid, created.Entity!.Uuid);

        Assert.NotEmpty(history);
        Assert.All(history, log => Assert.Equal(created.Entity.Uuid, log.ExpenseUuid));
        // The expense-create row precedes (or ties, then id-ordered) the share-create rows.
        Assert.Equal(AuditEntityType.Expense, history[0].EntityType);
        Assert.Equal(AuditAction.Create, history[0].Action);
    }

    [SkippableFact]
    public async Task ListByExpenseAsync_AfterCreateThenUpdate_IsOrderedCreateBeforeUpdate()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Ăn trưa"));
        await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, created.Entity!.Uuid,
            new UpdateExpenseData("Ăn tối", null, Noon, null, null, []));

        var history = await CreateAuditLogRepository().ListByExpenseAsync(ledger.User.Uuid, created.Entity.Uuid);

        var actions = history.Select(log => log.Action).ToList();
        Assert.Contains(AuditAction.Create, actions);
        Assert.Equal(AuditAction.Update, actions[^1]); // the later Update comes last (created_at ASC)
    }

    [SkippableFact]
    public async Task ListByExpenseAsync_ForeignUser_ReturnsEmptyList()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());

        var history = await CreateAuditLogRepository().ListByExpenseAsync(stranger.User.Uuid, created.Entity!.Uuid);

        Assert.Empty(history); // scoped by actor: a foreign uuid leaks nothing (OQ17)
    }

    [SkippableFact]
    public async Task ListByExpenseAsync_UnknownExpenseUuid_ReturnsEmptyList()
    {
        var ledger = await SeedLedgerAsync();

        var history = await CreateAuditLogRepository().ListByExpenseAsync(ledger.User.Uuid, "no-such-expense");

        Assert.Empty(history);
    }

    [SkippableFact]
    public async Task ListByExpenseAsync_AfterExpenseHardDeleted_StillReturnsCreateAndDeleteRows()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());
        var expenseUuid = created.Entity!.Uuid;

        await CreateExpenseRepository().DeleteAsync(ledger.User.Uuid, expenseUuid);

        // The expense row is gone, but its history persists (§3.8) - expense_uuid is a plain no-FK value.
        var history = await CreateAuditLogRepository().ListByExpenseAsync(ledger.User.Uuid, expenseUuid);

        Assert.Contains(history, log => log.Action == AuditAction.Create);
        Assert.Contains(history, log => log.Action == AuditAction.Delete);
    }
}
