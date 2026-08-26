using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <see cref="QrCorrelationCodeRepository"/> against the real MariaDB (skippable) -
/// planning/bank-callback-settlement.md Step 10. Proves the OQ2 find-or-reuse tuple match (identical
/// tuple reuses the code; any differing field or an expired prior code creates a fresh one), the
/// defensive owner-scoped resolution in <c>GetOrCreateAsync</c>, and <c>ResolveCurrentTargetAsync</c>'s
/// LIVE re-resolution (a <c>Share</c> target's current <c>Amount</c>, not the stale
/// <c>ExpectedAmountSnapshot</c>; an <c>EventMember</c> target's live <c>NetOwed</c> via
/// <c>EventSettlementClassifier</c>) plus its null-safe outcomes (unknown code, expired code).
/// </summary>
[Collection("AuthIntegration")]
public class QrCorrelationCodeRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private QrCorrelationCodeRepository CreateRepository() => new(CreateContext());

    /// <summary>Seeds a standalone expense (no event) with one share for <paramref name="billedMemberId"/>.</summary>
    private async Task<Expense> SeedExpenseWithShareAsync(ulong userId, ulong payerMemberId, ulong categoryId, ulong billedMemberId, decimal amount)
    {
        await using var context = CreateContext();
        var expense = new Expense
        {
            UserId = userId, Name = "Ăn tối", ExpenseTime = Noon,
            PayerMemberId = payerMemberId, CategoryId = categoryId
        };
        expense.Shares.Add(new Share { MemberId = billedMemberId, Amount = amount });
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
        return expense;
    }

    /// <summary>Seeds an event-scoped expense making every listed member a participant (mirrors EventMemberSettlementRepositoryTests).</summary>
    private async Task SeedEventExpenseAsync(ulong userId, ulong payerMemberId, ulong categoryId, ulong eventId, params (ulong MemberId, decimal Amount)[] shares)
    {
        await using var context = CreateContext();
        var expense = new Expense
        {
            UserId = userId, Name = "Chi tiêu", ExpenseTime = Noon,
            PayerMemberId = payerMemberId, CategoryId = categoryId, EventId = eventId
        };
        foreach (var (memberId, amount) in shares)
            expense.Shares.Add(new Share { MemberId = memberId, Amount = amount });
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
    }

    private async Task<QrCorrelationCode?> ReloadCodeAsync(string code)
    {
        await using var context = CreateContext();
        return await context.QrCorrelationCodes.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
    }

    private async Task ExpireCodeAsync(string code)
    {
        await using var context = CreateContext();
        await context.QrCorrelationCodes
            .Where(c => c.Code == code)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.ExpiresAt, AppDateTime.Now.AddDays(-1)));
    }

    // ---- GetOrCreateAsync: OQ2 find-or-reuse -----------------------------------------------------------

    [SkippableFact]
    public async Task GetOrCreateAsync_IdenticalTuple_ReusesTheSameExistingCode()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);

        var repository = CreateRepository();
        var first = await repository.GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);
        var second = await repository.GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);

        Assert.Equal(first.Code, second.Code);
        Assert.Equal(first.Id, second.Id);

        await using var context = CreateContext();
        Assert.Equal(1, await context.QrCorrelationCodes.CountAsync(c => c.MemberId == binh.Id)); // no duplicate row
    }

    [SkippableFact]
    public async Task GetOrCreateAsync_DifferentAmount_CreatesADistinctCode()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);

        var repository = CreateRepository();
        var first = await repository.GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);
        var second = await repository.GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 300_000m); // different amount

        Assert.NotEqual(first.Code, second.Code);
    }

    [SkippableFact]
    public async Task GetOrCreateAsync_DifferentMember_CreatesADistinctCode()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var chi = await SeedMemberAsync(ledger.User.Id, "Chi");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);
        await using (var context = CreateContext())
        {
            context.Shares.Add(new Share { ExpenseId = expense.Id, MemberId = chi.Id, Amount = 500_000m });
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository();
        var first = await repository.GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);
        var second = await repository.GetOrCreateAsync(ledger.User.Uuid, null, chi.Uuid, expense.Uuid, 500_000m);

        Assert.NotEqual(first.Code, second.Code);
    }

    [SkippableFact]
    public async Task GetOrCreateAsync_PriorCodeExpired_CreatesAFreshCode()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);

        var repository = CreateRepository();
        var first = await repository.GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);
        await ExpireCodeAsync(first.Code);

        var second = await repository.GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);

        Assert.NotEqual(first.Code, second.Code);
        var reloadedFirst = await ReloadCodeAsync(first.Code);
        Assert.NotNull(reloadedFirst); // the expired row is left in place (no cleanup job in v1, OQ7) - just superseded
    }

    [SkippableFact]
    public async Task GetOrCreateAsync_SetsA90DayExpiryOnANewCode()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);

        var created = await CreateRepository().GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);

        Assert.NotNull(created.ExpiresAt);
        var days = (created.ExpiresAt!.Value - AppDateTime.Now).TotalDays;
        Assert.InRange(days, 89.9, 90.1);
    }

    [SkippableFact]
    public async Task GetOrCreateAsync_EventTarget_SetsEventIdAndLeavesExpenseIdNull()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));

        var created = await CreateRepository().GetOrCreateAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, null, 500_000m);

        Assert.Equal(evt.Id, created.EventId);
        Assert.Null(created.ExpenseId);
    }

    [SkippableFact]
    public async Task GetOrCreateAsync_UnknownUser_ThrowsUnauthorized()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateRepository().GetOrCreateAsync("no-such-user-uuid", null, binh.Uuid, expense.Uuid, 500_000m));

        Assert.Equal(FairShareMonApi.Constants.ErrorCodes.Unauthorized, exception.Code);
    }

    [SkippableFact]
    public async Task GetOrCreateAsync_ForeignMember_ThrowsMemberNotFound()
    {
        var owner = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var strangerMember = await SeedMemberAsync(stranger.User.Id, "Người lạ");
        var expense = await SeedExpenseWithShareAsync(owner.User.Id, owner.OwnerRep.Id, owner.DefaultCategory.Id, owner.OwnerRep.Id, 500_000m);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateRepository().GetOrCreateAsync(owner.User.Uuid, null, strangerMember.Uuid, expense.Uuid, 500_000m));

        Assert.Equal(FairShareMonApi.Constants.ErrorCodes.MemberNotFound, exception.Code); // never leaks a foreign member
    }

    // ---- ResolveCurrentTargetAsync: LIVE re-resolution, never the snapshot ----------------------------

    [SkippableFact]
    public async Task ResolveCurrentTargetAsync_ShareTarget_UsesLiveShareAmountNotStaleSnapshot()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);
        var code = await CreateRepository().GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);

        // Edit the share's amount directly AFTER the code was generated (mirrors editing an expense).
        await using (var context = CreateContext())
        {
            await context.Shares.Where(s => s.ExpenseId == expense.Id && s.MemberId == binh.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Amount, 300_000m));
        }

        var target = await CreateRepository().ResolveCurrentTargetAsync(code.Code);

        Assert.NotNull(target);
        Assert.Equal(CorrelationTargetKind.Share, target!.Kind);
        Assert.Equal(300_000m, target.CurrentExpectedAmount); // LIVE amount, not the 500_000 snapshot
        Assert.False(target.IsAlreadySettled);
        Assert.Equal(expense.Uuid, target.ExpenseUuid);
        Assert.Equal(ledger.User.Uuid, target.UserUuid);
    }

    [SkippableFact]
    public async Task ResolveCurrentTargetAsync_ShareTarget_AlreadySettledReflectsLiveFlag()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);
        var code = await CreateRepository().GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);

        await using (var context = CreateContext())
        {
            await context.Shares.Where(s => s.ExpenseId == expense.Id && s.MemberId == binh.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsSettled, true));
        }

        var target = await CreateRepository().ResolveCurrentTargetAsync(code.Code);

        Assert.NotNull(target);
        Assert.True(target!.IsAlreadySettled);
    }

    [SkippableFact]
    public async Task ResolveCurrentTargetAsync_EventMemberTarget_UsesLiveNetOwedViaClassifier()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));
        var code = await CreateRepository().GetOrCreateAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, null, 500_000m);

        var target = await CreateRepository().ResolveCurrentTargetAsync(code.Code);

        Assert.NotNull(target);
        Assert.Equal(CorrelationTargetKind.EventMember, target!.Kind);
        Assert.Equal(500_000m, target.CurrentExpectedAmount); // Bình's live NetOwed
        Assert.False(target.IsAlreadySettled);
        Assert.Equal(evt.Uuid, target.EventUuid);
        Assert.Null(target.ExpenseUuid);
        Assert.Null(target.ShareUuid);
    }

    [SkippableFact]
    public async Task ResolveCurrentTargetAsync_EventMemberTarget_ClearedAmountReachingNetOwed_IsAlreadySettled()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        await SeedEventExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id, (binh.Id, 500_000m));
        var code = await CreateRepository().GetOrCreateAsync(ledger.User.Uuid, evt.Uuid, binh.Uuid, null, 500_000m);

        // Direction 2 credited the member's full net debt via partial-credit (mirrors event-expense-settlement-sync).
        await using (var context = CreateContext())
        {
            context.EventMemberSettlements.Add(new EventMemberSettlement
            {
                EventId = evt.Id, MemberId = binh.Id, ClearedAmount = 500_000m
            });
            await context.SaveChangesAsync();
        }

        var target = await CreateRepository().ResolveCurrentTargetAsync(code.Code);

        Assert.NotNull(target);
        Assert.True(target!.IsAlreadySettled); // ClearedAmount >= NetOwed
    }

    [SkippableFact]
    public async Task ResolveCurrentTargetAsync_UnknownCode_ReturnsNull()
    {
        var target = await CreateRepository().ResolveCurrentTargetAsync("FSMNOSUCH");

        Assert.Null(target);
    }

    [SkippableFact]
    public async Task ResolveCurrentTargetAsync_ExpiredCode_ReturnsNull()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await SeedExpenseWithShareAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, binh.Id, 500_000m);
        var code = await CreateRepository().GetOrCreateAsync(ledger.User.Uuid, null, binh.Uuid, expense.Uuid, 500_000m);
        await ExpireCodeAsync(code.Code);

        var target = await CreateRepository().ResolveCurrentTargetAsync(code.Code);

        Assert.Null(target); // degrades to "unmatched", safe (OQ2/Requirements)
    }

    // Defensive cleanup: sweep qr_correlation_codes by the prefix's users BEFORE the base class deletes
    // those users/members - qr_correlation_codes.member_id is RESTRICT (mirrors EventMemberSettlement.
    // MemberId), so a stray row must never survive to race the member cascade at user-delete time.
    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            await using var context = CreateContext();
            var userIds = await context.Users
                .Where(user => user.Username.StartsWith(UsernamePrefix))
                .Select(user => user.Id)
                .ToListAsync();

            await context.QrCorrelationCodes.Where(code => userIds.Contains(code.UserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
