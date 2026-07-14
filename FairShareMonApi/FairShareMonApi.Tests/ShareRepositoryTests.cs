using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>ShareRepository</c> against the real MariaDB (skippable). Covers the
/// share sub-route writes: add (link-validate member 7001, duplicate 7003), update (amount/note,
/// change-member allowed, owner-rep member-change guard 7002, duplicate 7003, no-op → no audit),
/// delete (owner-rep share protection 7002, hard-delete + Delete audit), the resource-owned scoping
/// via the owning expense, and that each mutation stages the matching audit row in its transaction.
/// </summary>
[Collection("AuthIntegration")]
public class ShareRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    private async Task<Expense> CreateExpenseAsync(Ledger ledger, params CreateShareData[] shares)
    {
        var data = new CreateExpenseData("Ăn trưa", null, Noon, null, null, [], shares);
        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, data);
        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        return result.Entity!;
    }

    private async Task<Share> ShareForMemberAsync(ulong expenseId, ulong memberId)
    {
        await using var context = CreateContext();
        return await context.Shares.AsNoTracking().SingleAsync(share => share.ExpenseId == expenseId && share.MemberId == memberId);
    }

    // ---- Add ---------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task AddAsync_ValidMember_PersistsShareAndStagesCreateAudit()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var expense = await CreateExpenseAsync(ledger); // owner-rep 0đ auto-injected

        var result = await CreateShareRepository().AddAsync(ledger.User.Uuid, expense.Uuid,
            new ShareData(friend.Uuid, 40_000m, "Nợ"));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        Assert.Equal(2, await context.Shares.CountAsync(share => share.ExpenseId == expense.Id));
        Assert.Equal(1, await context.AuditLogs.CountAsync(log =>
            log.ExpenseUuid == expense.Uuid && log.EntityType == AuditEntityType.Share && log.Action == AuditAction.Create
            && log.EntityUuid == result.Entity!.Uuid));
    }

    [SkippableFact]
    public async Task AddAsync_ForeignMember_ReturnsShareMemberInvalid()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedUserAsync();
        var strangerMember = await SeedMemberAsync(stranger.Id, "Ngoài");
        var expense = await CreateExpenseAsync(ledger);

        var result = await CreateShareRepository().AddAsync(ledger.User.Uuid, expense.Uuid,
            new ShareData(strangerMember.Uuid, 10_000m, null));

        Assert.Equal(ExpenseWriteStatus.ShareMemberInvalid, result.Status);
    }

    [SkippableFact]
    public async Task AddAsync_DuplicateMember_ReturnsDuplicateShareMember()
    {
        var ledger = await SeedLedgerAsync();
        var expense = await CreateExpenseAsync(ledger); // already has the owner-rep share

        var result = await CreateShareRepository().AddAsync(ledger.User.Uuid, expense.Uuid,
            new ShareData(ledger.OwnerRep.Uuid, 10_000m, null)); // owner-rep already has a share

        Assert.Equal(ExpenseWriteStatus.DuplicateShareMember, result.Status);
    }

    [SkippableFact]
    public async Task AddAsync_AnotherUsersExpense_ReturnsExpenseNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var expense = await CreateExpenseAsync(ledger);

        var result = await CreateShareRepository().AddAsync(stranger.User.Uuid, expense.Uuid,
            new ShareData(stranger.OwnerRep.Uuid, 10_000m, null));

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, result.Status); // resource-owned via the expense
    }

    // ---- Update ------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task UpdateAsync_AmountChange_PersistsAndStagesUpdateAudit()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, null));
        var share = await ShareForMemberAsync(expense.Id, friend.Id);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, share.Uuid,
            new ShareData(friend.Uuid, 55_000m, "Sửa"));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        var persisted = await context.Shares.AsNoTracking().SingleAsync(s => s.Id == share.Id);
        Assert.Equal(55_000m, persisted.Amount);
        Assert.Equal(1, await context.AuditLogs.CountAsync(log =>
            log.EntityUuid == share.Uuid && log.EntityType == AuditEntityType.Share && log.Action == AuditAction.Update));
    }

    [SkippableFact]
    public async Task UpdateAsync_ChangeMember_IsAllowedForRegularShare()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var other = await SeedMemberAsync(ledger.User.Id, "Bình");
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, null));
        var share = await ShareForMemberAsync(expense.Id, friend.Id);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, share.Uuid,
            new ShareData(other.Uuid, 40_000m, null)); // change member away

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        Assert.Equal(other.Id, (await context.Shares.AsNoTracking().SingleAsync(s => s.Id == share.Id)).MemberId);
    }

    [SkippableFact]
    public async Task UpdateAsync_ChangeOwnerRepShareMemberAway_ReturnsOwnerRepresentativeShareNotDeletable()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var expense = await CreateExpenseAsync(ledger);
        var ownerRepShare = await ShareForMemberAsync(expense.Id, ledger.OwnerRep.Id);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, ownerRepShare.Uuid,
            new ShareData(friend.Uuid, 10_000m, null)); // trying to change the owner-rep's member

        Assert.Equal(ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable, result.Status); // §5 (7002)
    }

    [SkippableFact]
    public async Task UpdateAsync_OwnerRepShareAmountChange_IsAllowed()
    {
        var ledger = await SeedLedgerAsync();
        var expense = await CreateExpenseAsync(ledger);
        var ownerRepShare = await ShareForMemberAsync(expense.Id, ledger.OwnerRep.Id);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, ownerRepShare.Uuid,
            new ShareData(ledger.OwnerRep.Uuid, 25_000m, null)); // same member, new amount

        Assert.Equal(ExpenseWriteStatus.Success, result.Status); // amount/note editable (OQ4)
        await using var context = CreateContext();
        Assert.Equal(25_000m, (await context.Shares.AsNoTracking().SingleAsync(s => s.Id == ownerRepShare.Id)).Amount);
    }

    [SkippableFact]
    public async Task UpdateAsync_ChangeToDuplicateMember_ReturnsDuplicateShareMember()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        // Both owner-rep and friend already have shares.
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, null));
        var friendShare = await ShareForMemberAsync(expense.Id, friend.Id);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, friendShare.Uuid,
            new ShareData(ledger.OwnerRep.Uuid, 40_000m, null)); // owner-rep already has a share

        Assert.Equal(ExpenseWriteStatus.DuplicateShareMember, result.Status);
    }

    /// <summary>
    /// FAILING - documents a confirmed production bug (see the test-engineer's report). A genuine no-op
    /// share update must write NO audit row (OQ9), but the <c>AuditLogFactory</c> compares raw
    /// serialized snapshots and <c>ShareAuditSnapshot.Amount</c> is not canonicalized: the DB-loaded
    /// before-value is DECIMAL(18,2) (serializes "40000.00") while the request after-value has scale 0
    /// (serializes "40000"), so the snapshots differ and a spurious Update row is written on a true
    /// no-op.
    /// </summary>
    [SkippableFact]
    public async Task UpdateAsync_NoChange_WritesNoAuditRow()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, "Nợ"));
        var share = await ShareForMemberAsync(expense.Id, friend.Id);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, share.Uuid,
            new ShareData(friend.Uuid, 40_000m, "Nợ")); // identical

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        Assert.Equal(0, await context.AuditLogs.CountAsync(log => log.EntityUuid == share.Uuid && log.Action == AuditAction.Update));
    }

    [SkippableFact]
    public async Task UpdateAsync_ForeignMemberChange_ReturnsShareMemberInvalid()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var stranger = await SeedUserAsync();
        var strangerMember = await SeedMemberAsync(stranger.Id, "Ngoài");
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, null));
        var share = await ShareForMemberAsync(expense.Id, friend.Id);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, share.Uuid,
            new ShareData(strangerMember.Uuid, 40_000m, null));

        Assert.Equal(ExpenseWriteStatus.ShareMemberInvalid, result.Status);
    }

    [SkippableFact]
    public async Task UpdateAsync_UnknownShare_ReturnsShareNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var expense = await CreateExpenseAsync(ledger);

        var result = await CreateShareRepository().UpdateAsync(ledger.User.Uuid, expense.Uuid, "no-such-share",
            new ShareData(ledger.OwnerRep.Uuid, 10_000m, null));

        Assert.Equal(ExpenseWriteStatus.ShareNotFound, result.Status);
    }

    [SkippableFact]
    public async Task UpdateAsync_AnotherUsersExpense_ReturnsShareNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, null));
        var share = await ShareForMemberAsync(expense.Id, friend.Id);

        var result = await CreateShareRepository().UpdateAsync(stranger.User.Uuid, expense.Uuid, share.Uuid,
            new ShareData(stranger.OwnerRep.Uuid, 10_000m, null));

        Assert.Equal(ExpenseWriteStatus.ShareNotFound, result.Status); // resource-owned via the expense
    }

    // ---- Delete ------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task DeleteAsync_RegularShare_HardRemovesAndStagesDeleteAudit()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, null));
        var share = await ShareForMemberAsync(expense.Id, friend.Id);

        var status = await CreateShareRepository().DeleteAsync(ledger.User.Uuid, expense.Uuid, share.Uuid);

        Assert.Equal(ExpenseWriteStatus.Success, status);
        await using var context = CreateContext();
        Assert.Equal(0, await context.Shares.CountAsync(s => s.Id == share.Id)); // hard-deleted
        Assert.Equal(1, await context.AuditLogs.CountAsync(log => log.EntityUuid == share.Uuid && log.Action == AuditAction.Delete));
    }

    [SkippableFact]
    public async Task DeleteAsync_OwnerRepShare_ReturnsOwnerRepresentativeShareNotDeletable()
    {
        var ledger = await SeedLedgerAsync();
        var expense = await CreateExpenseAsync(ledger);
        var ownerRepShare = await ShareForMemberAsync(expense.Id, ledger.OwnerRep.Id);

        var status = await CreateShareRepository().DeleteAsync(ledger.User.Uuid, expense.Uuid, ownerRepShare.Uuid);

        Assert.Equal(ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable, status); // §5 (7002)
        await using var context = CreateContext();
        Assert.Equal(1, await context.Shares.CountAsync(s => s.Id == ownerRepShare.Id)); // still there
    }

    [SkippableFact]
    public async Task DeleteAsync_UnknownShare_ReturnsShareNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var expense = await CreateExpenseAsync(ledger);

        var status = await CreateShareRepository().DeleteAsync(ledger.User.Uuid, expense.Uuid, "no-such-share");

        Assert.Equal(ExpenseWriteStatus.ShareNotFound, status);
    }

    [SkippableFact]
    public async Task DeleteAsync_AnotherUsersExpense_ReturnsShareNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var expense = await CreateExpenseAsync(ledger, new CreateShareData(friend.Uuid, 40_000m, null));
        var share = await ShareForMemberAsync(expense.Id, friend.Id);

        var status = await CreateShareRepository().DeleteAsync(stranger.User.Uuid, expense.Uuid, share.Uuid);

        Assert.Equal(ExpenseWriteStatus.ShareNotFound, status);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Shares.CountAsync(s => s.Id == share.Id)); // untouched
    }
}
