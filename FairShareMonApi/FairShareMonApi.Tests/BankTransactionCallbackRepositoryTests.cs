using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <see cref="BankTransactionCallbackRepository"/> against the real MariaDB
/// (skippable) - planning/bank-callback-settlement.md Step 10. Proves the idempotency dedup pre-check,
/// the unique <c>(provider_key, provider_transaction_id)</c> index's DB-level backstop against a
/// concurrent-insert race (never surfaced as a 500 - resolves to the existing row), and
/// <c>ListByUserAsync</c>'s owner scoping/pagination.
/// </summary>
[Collection("AuthIntegration")]
public class BankTransactionCallbackRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    // provider_transaction_id has NO FK back to any prefix'd user (a row can legitimately have
    // resolved_user_id = null, e.g. UnmatchedCode), so - unlike UsernamePrefix - this is the only handle
    // DisposeAsync has to find and sweep every row THIS test class instance created, across repeated runs.
    private readonly string _txPrefix = "btx" + Guid.NewGuid().ToString("N")[..10] + "_";

    private BankTransactionCallbackRepository CreateRepository() => new(CreateContext());

    private string Tx(string suffix) => _txPrefix + suffix;

    private static BankTransactionCallbackData Data(
        string providerTransactionId,
        string providerKey = "sepay",
        decimal amount = 500_000m,
        string content = "FSM8K2QX7 chuyen tien",
        ulong? resolvedUserId = null,
        BankCallbackOutcome outcome = BankCallbackOutcome.Applied,
        ulong? matchedCorrelationCodeId = null) =>
        new(providerKey, providerTransactionId, true, amount, null, null, content, "FSM8K2QX7",
            DateTime.UtcNow, "{\"id\":1}", matchedCorrelationCodeId, resolvedUserId, outcome, null,
            outcome == BankCallbackOutcome.Applied ? DateTime.UtcNow : null);

    // ---- FindByProviderTransactionAsync: the idempotency pre-check --------------------------------------

    [SkippableFact]
    public async Task FindByProviderTransactionAsync_Existing_ReturnsTheRow()
    {
        var repository = CreateRepository();
        var recorded = await repository.RecordAsync(Data(Tx("find-1")));

        var found = await repository.FindByProviderTransactionAsync("sepay", Tx("find-1"));

        Assert.NotNull(found);
        Assert.Equal(recorded.Id, found!.Id);
    }

    [SkippableFact]
    public async Task FindByProviderTransactionAsync_Missing_ReturnsNull()
    {
        var found = await CreateRepository().FindByProviderTransactionAsync("sepay", Tx("no-such-tx"));

        Assert.Null(found);
    }

    [SkippableFact]
    public async Task FindByProviderTransactionAsync_SameTransactionIdDifferentProvider_ReturnsNull()
    {
        var repository = CreateRepository();
        await repository.RecordAsync(Data(Tx("shared-id"), providerKey: "sepay"));

        var found = await repository.FindByProviderTransactionAsync("otherbank", Tx("shared-id"));

        Assert.Null(found); // the dedup key is the COMPOSITE (provider_key, provider_transaction_id)
    }

    // ---- RecordAsync: unique-index race handling -------------------------------------------------------

    [SkippableFact]
    public async Task RecordAsync_UniqueIndexEnforced_ConcurrentDuplicateInsertReturnsExistingRowNeverThrows()
    {
        var repository = CreateRepository();
        var first = await repository.RecordAsync(Data(Tx("race-1"), amount: 500_000m));

        // Simulates a race where the idempotency pre-check missed the row a split second before another
        // request inserted it - the DB-level unique index is the backstop (Requirements/Decision Log).
        var second = await repository.RecordAsync(Data(Tx("race-1"), amount: 999_999m));

        Assert.Equal(first.Id, second.Id); // resolves to the FIRST recorded row, never a 500/duplicate-key throw
        await using var context = CreateContext();
        Assert.Equal(1, await context.BankTransactionCallbacks.CountAsync(c => c.ProviderTransactionId == Tx("race-1")));
    }

    [SkippableFact]
    public async Task RecordAsync_DistinctTransactionIds_InsertsSeparateRows()
    {
        var repository = CreateRepository();
        var first = await repository.RecordAsync(Data(Tx("distinct-1")));
        var second = await repository.RecordAsync(Data(Tx("distinct-2")));

        Assert.NotEqual(first.Id, second.Id);
    }

    // ---- ListByUserAsync: owner scoping + pagination ---------------------------------------------------

    [SkippableFact]
    public async Task ListByUserAsync_ScopedToOwner_AnotherUsersRowsNeverAppear()
    {
        var owner = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var repository = CreateRepository();
        await repository.RecordAsync(Data(Tx("owner-1"), resolvedUserId: owner.User.Id));
        await repository.RecordAsync(Data(Tx("stranger-1"), resolvedUserId: stranger.User.Id));

        var (items, total) = await repository.ListByUserAsync(owner.User.Uuid, limit: 20, offset: 0);

        Assert.Equal(1, total);
        var only = Assert.Single(items);
        Assert.Equal(Tx("owner-1"), only.ProviderTransactionId);
    }

    [SkippableFact]
    public async Task ListByUserAsync_UnresolvedUserRows_NeverAppearForAnyOwner()
    {
        var owner = await SeedLedgerAsync();
        var repository = CreateRepository();
        await repository.RecordAsync(Data(Tx("unmatched-1"), resolvedUserId: null, outcome: BankCallbackOutcome.UnmatchedCode));

        var (items, total) = await repository.ListByUserAsync(owner.User.Uuid, limit: 20, offset: 0);

        Assert.Equal(0, total);
        Assert.Empty(items); // OQ5's known trade-off: a fully-unresolvable transaction is invisible to every owner
    }

    [SkippableFact]
    public async Task ListByUserAsync_NewestFirst_RespectsLimitAndOffset()
    {
        var owner = await SeedLedgerAsync();
        var repository = CreateRepository();
        for (var i = 1; i <= 3; i++)
        {
            await repository.RecordAsync(Data(Tx($"page-{i}"), resolvedUserId: owner.User.Id));
            await Task.Delay(15); // ensure distinct CreatedAt ordering
        }

        var (page1, total) = await repository.ListByUserAsync(owner.User.Uuid, limit: 2, offset: 0);
        var (page2, _) = await repository.ListByUserAsync(owner.User.Uuid, limit: 2, offset: 2);

        Assert.Equal(3, total);
        Assert.Equal(2, page1.Count);
        Assert.Equal(Tx("page-3"), page1[0].ProviderTransactionId); // newest first
        Assert.Equal(Tx("page-2"), page1[1].ProviderTransactionId);
        var onlyLeftover = Assert.Single(page2);
        Assert.Equal(Tx("page-1"), onlyLeftover.ProviderTransactionId);
    }

    // Defensive cleanup: sweep every row this instance created (by its OWN provider-transaction-id prefix,
    // since a row can legitimately carry no resolved_user_id/no FK to any prefix'd user) BEFORE the base
    // class deletes the seeded users.
    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            await using var context = CreateContext();
            await context.BankTransactionCallbacks
                .Where(callback => callback.ProviderTransactionId.StartsWith(_txPrefix))
                .ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
