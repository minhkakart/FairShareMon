using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Stats;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for the Layer B overlay load in <c>StatsRepository.GetEventBalanceAsync</c> against
/// the real MariaDB (skippable). Proves the additive enrichment (settled-per-member OQ8a): a member's
/// <c>event_member_settlements</c> flag is surfaced on the balance aggregate, defaults false/null for a
/// participant with no row, and does NOT perturb <c>advanced</c>/<c>owed</c>/balance - the M7 sum-to-zero
/// balance stays byte-identical whether or not a settled flag is present (D2 / M7 OQ2 regression). A
/// soft-deleted participant still carries its overlay (§4.7). The overlay MATH (outstanding, counts) lives
/// in <c>StatsService</c> and is covered by its unit tests; this asserts the pure repository load.
/// </summary>
[Collection("AuthIntegration")]
public class EventBalanceOverlayRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private StatsRepository CreateStatsRepository() => new(CreateContext());

    private async Task SeedExpenseAsync(ulong userId, ulong payerMemberId, ulong categoryId, ulong eventId, params (ulong MemberId, decimal Amount)[] shares)
    {
        await using var context = CreateContext();
        var expense = new Expense
        {
            UserId = userId,
            Name = "Chi tiêu",
            ExpenseTime = Day15Noon,
            PayerMemberId = payerMemberId,
            CategoryId = categoryId,
            EventId = eventId
        };
        foreach (var (memberId, amount) in shares)
            expense.Shares.Add(new Share { MemberId = memberId, Amount = amount });
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();
    }

    private async Task MarkSettledAsync(ulong eventId, ulong memberId)
    {
        await using var context = CreateContext();
        context.EventMemberSettlements.Add(new EventMemberSettlement
        {
            EventId = eventId,
            MemberId = memberId,
            IsSettled = true,
            SettledAt = new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();
    }

    private static MemberBalanceAggregate Row(IReadOnlyList<MemberBalanceAggregate> rows, string uuid) =>
        Assert.Single(rows, row => row.MemberUuid == uuid);

    /// <summary>The §3.7 scenario: owner-rep advanced 300k (owes 0 in this expense), Bình +?, Cường −500k.</summary>
    private async Task<(Ledger Ledger, Member Binh, Member Cuong, Event Evt)> SeedScenarioAsync()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình");
        var cuong = await SeedMemberAsync(ledger.User.Id, "Cường");
        var evt = await SeedEventAsync(ledger.User.Id, "Đà Lạt", Day14, Day16);
        // Bình advanced 800k; An advanced 700k; Cường advanced 0. Everyone owes 500k → Cường −500k, Bình +300k, An +200k.
        await SeedExpenseAsync(ledger.User.Id, binh.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 300_000m), (binh.Id, 200_000m), (cuong.Id, 300_000m));
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 200_000m), (binh.Id, 300_000m), (cuong.Id, 200_000m));
        return (ledger, binh, cuong, evt);
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_NoSettlementRows_AllFlagsDefaultFalse()
    {
        var (ledger, binh, cuong, evt) = await SeedScenarioAsync();

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        Assert.All(rows, row => Assert.False(row.IsSettled));
        Assert.All(rows, row => Assert.Null(row.SettledAt));
        _ = binh; _ = cuong;
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_SettledMember_CarriesFlagOnlyForThatMember()
    {
        var (ledger, binh, cuong, evt) = await SeedScenarioAsync();
        await MarkSettledAsync(evt.Id, cuong.Id);

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        var cuongRow = Row(rows, cuong.Uuid);
        Assert.True(cuongRow.IsSettled);
        Assert.NotNull(cuongRow.SettledAt);
        Assert.False(Row(rows, binh.Uuid).IsSettled);            // untouched member
        Assert.False(Row(rows, ledger.OwnerRep.Uuid).IsSettled);
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_SettledFlag_LeavesAdvancedOwedBalanceIdentical()
    {
        var (ledger, _, cuong, evt) = await SeedScenarioAsync();

        var before = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);
        await MarkSettledAsync(evt.Id, cuong.Id); // mark Cường settled (Layer B)
        var after = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        // Every member's advanced/owed/balance is byte-identical before and after the settled flag (D2 / M7 OQ2).
        foreach (var row in before)
        {
            var match = Row(after, row.MemberUuid);
            Assert.Equal(row.Advanced, match.Advanced);
            Assert.Equal(row.Owed, match.Owed);
            Assert.Equal(row.Advanced - row.Owed, match.Advanced - match.Owed);
        }
        Assert.Equal(0m, after.Sum(row => row.Advanced - row.Owed)); // sum-to-zero preserved
    }

    [SkippableFact]
    public async Task GetEventBalanceAsync_SoftDeletedSettledParticipant_StillAppearsWithOverlay()
    {
        var ledger = await SeedLedgerAsync();
        var binh = await SeedMemberAsync(ledger.User.Id, "Bình", deleted: true);
        var evt = await SeedEventAsync(ledger.User.Id, "Đợt cũ", Day14, Day16);
        await SeedExpenseAsync(ledger.User.Id, ledger.OwnerRep.Id, ledger.DefaultCategory.Id, evt.Id,
            (ledger.OwnerRep.Id, 300_000m), (binh.Id, 100_000m)); // Bình owes 100k → −100k
        await MarkSettledAsync(evt.Id, binh.Id);

        var rows = await CreateStatsRepository().GetEventBalanceAsync(ledger.User.Uuid, evt.Id);

        var binhRow = Row(rows, binh.Uuid);
        Assert.True(binhRow.IsDeleted);   // §4.7 - still in the historical balance
        Assert.True(binhRow.IsSettled);   // with its overlay flag
        Assert.Equal(-100_000m, binhRow.Advanced - binhRow.Owed); // balance unchanged
    }
}
