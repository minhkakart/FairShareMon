using System.Text.Json;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>ExpenseRepository</c> against the real MariaDB (skippable). Covers the
/// atomic create-with-shares-tags-audit (§4.5) and its all-or-nothing rollback, the create defaults
/// (owner-rep payer / default category / auto-injected 0đ owner-rep share, OQ4), resource-owned
/// scoping (404 semantics, never the row), §4.2/§4.8 link integrity (foreign/soft-deleted
/// payer/category/tag/share-member rejected) with §4.7 history-still-displays-deleted-links, the
/// money CHECK (amount ≥ 0) + derived total, duplicate-member rejection (OQ5), hard-delete cascade
/// with surviving audit (§3.8), the audit granularity/no-op/settled-no-audit rules, and the
/// list filters + expense_time DESC sort (OQ13).
/// </summary>
[Collection("AuthIntegration")]
public class ExpenseRepositoryTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly DateTime Noon = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CreateExpenseData CreateData(
        string name = "Ăn trưa",
        string? description = null,
        DateTime? expenseTime = null,
        string? payerUuid = null,
        string? categoryUuid = null,
        IReadOnlyList<string>? tagUuids = null,
        IReadOnlyList<CreateShareData>? shares = null) =>
        new(name, description, expenseTime ?? Noon, payerUuid, categoryUuid, tagUuids ?? [], shares ?? []);

    private async Task<int> CountExpensesAsync(ulong userId)
    {
        await using var context = CreateContext();
        return await context.Expenses.CountAsync(expense => expense.UserId == userId);
    }

    private async Task<int> CountAuditAsync(ulong actorUserId)
    {
        await using var context = CreateContext();
        return await context.AuditLogs.CountAsync(log => log.ActorUserId == actorUserId);
    }

    // ---- Create: defaults + atomicity --------------------------------------------------------------

    [SkippableFact]
    public async Task CreateAsync_OmittedPayerAndCategory_UsesOwnerRepAndDefaultCategory()
    {
        var ledger = await SeedLedgerAsync();

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            CreateData(shares: [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)]));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        var persisted = await context.Expenses.AsNoTracking().SingleAsync(expense => expense.Uuid == result.Entity!.Uuid);
        Assert.Equal(ledger.OwnerRep.Id, persisted.PayerMemberId); // default payer = owner-rep
        Assert.Equal(ledger.DefaultCategory.Id, persisted.CategoryId); // default category
    }

    [SkippableFact]
    public async Task CreateAsync_OwnerRepShareOmitted_AutoInjectsZeroAmountShare()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            CreateData(payerUuid: friend.Uuid, shares: [new CreateShareData(friend.Uuid, 100_000m, null)]));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        var shares = await context.Shares.AsNoTracking().Where(share => share.ExpenseId == result.Entity!.Id).ToListAsync();
        Assert.Equal(2, shares.Count); // friend + auto-injected owner-rep
        var ownerRepShare = Assert.Single(shares, share => share.MemberId == ledger.OwnerRep.Id);
        Assert.Equal(0m, ownerRepShare.Amount); // §5: owner-rep 0đ share (OQ4)
    }

    [SkippableFact]
    public async Task CreateAsync_Success_PersistsExpenseSharesTagsAndAuditInOneUnit()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var tag = await SeedTagAsync(ledger.User.Id, "Công tác");

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(
            tagUuids: [tag.Uuid],
            shares: [new CreateShareData(ledger.OwnerRep.Uuid, 60_000m, null), new CreateShareData(friend.Uuid, 40_000m, "Nợ")]));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        var expenseId = result.Entity!.Id;
        Assert.Equal(2, await context.Shares.CountAsync(share => share.ExpenseId == expenseId));
        Assert.Equal(1, await context.ExpenseTags.CountAsync(link => link.ExpenseId == expenseId));
        // 1 Expense/Create + 2 Share/Create audit rows (OQ10).
        var audit = await context.AuditLogs.AsNoTracking().Where(log => log.ExpenseUuid == result.Entity.Uuid).ToListAsync();
        Assert.Equal(3, audit.Count);
        Assert.Equal(1, audit.Count(log => log.EntityType == AuditEntityType.Expense && log.Action == AuditAction.Create));
        Assert.Equal(2, audit.Count(log => log.EntityType == AuditEntityType.Share && log.Action == AuditAction.Create));
    }

    [SkippableFact]
    public async Task CreateAsync_ForeignShareMemberMidList_RollsBackEverything()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedUserAsync();
        var strangerMember = await SeedMemberAsync(stranger.Id, "Ngoài");
        var tag = await SeedTagAsync(ledger.User.Id, "Công tác");

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(
            tagUuids: [tag.Uuid],
            shares:
            [
                new CreateShareData(ledger.OwnerRep.Uuid, 60_000m, null),
                new CreateShareData(strangerMember.Uuid, 40_000m, null) // foreign -> rejected mid-list
            ]));

        Assert.Equal(ExpenseWriteStatus.ShareMemberInvalid, result.Status);
        // All-or-nothing (§4.5): no expense, no shares, no expense_tags, no audit.
        Assert.Equal(0, await CountExpensesAsync(ledger.User.Id));
        Assert.Equal(0, await CountAuditAsync(ledger.User.Id));
        await using var context = CreateContext();
        Assert.Equal(0, await context.ExpenseTags.CountAsync(link => link.Tag.UserId == ledger.User.Id));
    }

    // ---- Create: link integrity (§4.2/§4.8) --------------------------------------------------------

    [SkippableFact]
    public async Task CreateAsync_ForeignPayer_ReturnsPayerInvalid()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedUserAsync();
        var strangerMember = await SeedMemberAsync(stranger.Id, "Ngoài");

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(payerUuid: strangerMember.Uuid));

        Assert.Equal(ExpenseWriteStatus.PayerInvalid, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_SoftDeletedPayer_ReturnsPayerInvalid()
    {
        var ledger = await SeedLedgerAsync();
        var deletedMember = await SeedMemberAsync(ledger.User.Id, "Đã xóa", deleted: true);

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(payerUuid: deletedMember.Uuid));

        Assert.Equal(ExpenseWriteStatus.PayerInvalid, result.Status); // §4.8 not selectable when deleted
    }

    [SkippableFact]
    public async Task CreateAsync_SoftDeletedCategory_ReturnsCategoryInvalid()
    {
        var ledger = await SeedLedgerAsync();
        var deletedCategory = await SeedCategoryAsync(ledger.User.Id, "Đã xóa", deleted: true);

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(categoryUuid: deletedCategory.Uuid));

        Assert.Equal(ExpenseWriteStatus.CategoryInvalid, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_SoftDeletedTag_ReturnsTagInvalid()
    {
        var ledger = await SeedLedgerAsync();
        var deletedTag = await SeedTagAsync(ledger.User.Id, "Đã xóa", deleted: true);

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(tagUuids: [deletedTag.Uuid]));

        Assert.Equal(ExpenseWriteStatus.TagInvalid, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_DuplicateShareMembers_ReturnsDuplicateShareMember()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(shares:
        [
            new CreateShareData(friend.Uuid, 10_000m, null),
            new CreateShareData(friend.Uuid, 20_000m, null) // OQ5: one share per member
        ]));

        Assert.Equal(ExpenseWriteStatus.DuplicateShareMember, result.Status);
        Assert.Equal(0, await CountExpensesAsync(ledger.User.Id));
    }

    [SkippableFact]
    public async Task CreateAsync_UnknownUser_ReturnsExpenseNotFound()
    {
        var result = await CreateExpenseRepository().CreateAsync("00000000-0000-7000-8000-000000000000", CreateData());

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, result.Status);
    }

    // ---- §4.7: history keeps + displays a later-deleted link ----------------------------------------

    [SkippableFact]
    public async Task GetByUuidAsync_LinkedMemberSoftDeletedLater_StillDisplaysOnTheExpense()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            CreateData(shares: [new CreateShareData(friend.Uuid, 40_000m, null)]));
        Assert.Equal(ExpenseWriteStatus.Success, created.Status);

        // Soft-delete the member AFTER it was linked (§4.7).
        await using (var context = CreateContext())
        {
            var member = await context.Members.SingleAsync(m => m.Id == friend.Id);
            member.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        var loaded = await CreateExpenseRepository().GetByUuidAsync(ledger.User.Uuid, created.Entity!.Uuid);

        Assert.NotNull(loaded);
        var friendShare = Assert.Single(loaded!.Shares, share => share.MemberId == friend.Id);
        Assert.True(friendShare.Member.IsDeleted); // still linked + displays the deleted member
    }

    // ---- Resource-owned scoping --------------------------------------------------------------------

    [SkippableFact]
    public async Task GetByUuidAsync_AnotherUsersExpense_ReturnsNull()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            CreateData(shares: [new CreateShareData(ledger.OwnerRep.Uuid, 10_000m, null)]));

        var seenByStranger = await CreateExpenseRepository().GetByUuidAsync(stranger.User.Uuid, created.Entity!.Uuid);
        var seenByOwner = await CreateExpenseRepository().GetByUuidAsync(ledger.User.Uuid, created.Entity.Uuid);

        Assert.Null(seenByStranger); // resource-owned: existence not leaked
        Assert.NotNull(seenByOwner);
    }

    [SkippableFact]
    public async Task ListByUserAsync_ReturnsOnlyTheCallersExpenses()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Mine"));
        await CreateExpenseRepository().CreateAsync(stranger.User.Uuid, CreateData(name: "Theirs"));

        var list = await CreateExpenseRepository().ListByUserAsync(ledger.User.Uuid, new());

        Assert.Equal(["Mine"], list.Select(expense => expense.Name));
    }

    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_AnotherUsersExpense_ReturnsExpenseNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Mine"));

        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(stranger.User.Uuid, created.Entity!.Uuid,
            new UpdateExpenseData("Hacked", null, Noon, null, null, []));

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, result.Status);
        Assert.Equal("Mine", (await ReloadExpenseAsync(created.Entity.Uuid))!.Name); // untouched
    }

    [SkippableFact]
    public async Task DeleteAsync_AnotherUsersExpense_ReturnsExpenseNotFoundAndLeavesItIntact()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());

        var status = await CreateExpenseRepository().DeleteAsync(stranger.User.Uuid, created.Entity!.Uuid);

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, status);
        Assert.NotNull(await ReloadExpenseAsync(created.Entity.Uuid));
    }

    [SkippableFact]
    public async Task SetSettledAsync_AnotherUsersExpense_ReturnsExpenseNotFound()
    {
        var ledger = await SeedLedgerAsync();
        var stranger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());

        var status = await CreateExpenseRepository().SetSettledAsync(stranger.User.Uuid, created.Entity!.Uuid, true);

        Assert.Equal(ExpenseWriteStatus.ExpenseNotFound, status);
    }

    // ---- Update: link validation + audit -----------------------------------------------------------

    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_RealChange_WritesUpdateAuditWithBeforeAndAfter()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Ăn trưa"));

        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, created.Entity!.Uuid,
            new UpdateExpenseData("Ăn tối", null, Noon, null, null, []));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        var update = await context.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.ExpenseUuid == created.Entity.Uuid && log.Action == AuditAction.Update);
        // Parse rather than substring-match: System.Text.Json escapes non-ASCII (Vietnamese) in the raw JSON.
        using var before = JsonDocument.Parse(update.BeforeData!);
        using var after = JsonDocument.Parse(update.AfterData!);
        Assert.Equal("Ăn trưa", before.RootElement.GetProperty("name").GetString());
        Assert.Equal("Ăn tối", after.RootElement.GetProperty("name").GetString());
    }

    /// <summary>
    /// A genuine no-op expense update must write NO audit row (OQ9). This previously FAILED (a confirmed
    /// production bug): the DB-loaded before-value materialized as <c>DateTimeKind.Unspecified</c>
    /// (serialized "…T12:00:00") while the request after-value was <c>DateTimeKind.Utc</c> (serialized
    /// "…T12:00:00Z"), so the snapshots differed and a spurious Update row was written. Timezone-aware
    /// DateTimes FIXED it at the source: the global read-side <c>UtcDateTimeConverter</c>
    /// (<c>AppDbContext.ConfigureConventions</c>) now stamps every materialized <see cref="System.DateTime"/>
    /// with <c>Kind.Utc</c>, so both before/after snapshots serialize identically and the
    /// <c>AuditSnapshotCanonicalizer.Utc</c> workaround was removed. This test now PASSES via the converter.
    /// </summary>
    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_NoChange_WritesNoAuditRow()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            CreateData(name: "Ăn trưa", payerUuid: ledger.OwnerRep.Uuid, categoryUuid: ledger.DefaultCategory.Uuid));

        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, created.Entity!.Uuid,
            new UpdateExpenseData("Ăn trưa", null, Noon, ledger.OwnerRep.Uuid, ledger.DefaultCategory.Uuid, []));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        // No Update row - only the original create rows survive (OQ9).
        Assert.Equal(0, await context.AuditLogs.CountAsync(log => log.ExpenseUuid == created.Entity.Uuid && log.Action == AuditAction.Update));
    }

    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_TagSetFullReplace_AddsAndRemovesJoinRows()
    {
        var ledger = await SeedLedgerAsync();
        var tagA = await SeedTagAsync(ledger.User.Id, "A");
        var tagB = await SeedTagAsync(ledger.User.Id, "B");
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(tagUuids: [tagA.Uuid]));

        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, created.Entity!.Uuid,
            new UpdateExpenseData("Ăn trưa", null, Noon, null, null, [tagB.Uuid])); // replace A with B

        Assert.Equal(ExpenseWriteStatus.Success, result.Status);
        await using var context = CreateContext();
        var tagIds = await context.ExpenseTags.Where(link => link.ExpenseId == created.Entity.Id).Select(link => link.TagId).ToListAsync();
        Assert.Equal([tagB.Id], tagIds); // full replace (OQ18)
    }

    [SkippableFact]
    public async Task UpdateGeneralInfoAsync_SoftDeletedCategory_ReturnsCategoryInvalid()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());
        var deletedCategory = await SeedCategoryAsync(ledger.User.Id, "Đã xóa", deleted: true);

        var result = await CreateExpenseRepository().UpdateGeneralInfoAsync(ledger.User.Uuid, created.Entity!.Uuid,
            new UpdateExpenseData("Ăn trưa", null, Noon, null, deletedCategory.Uuid, []));

        Assert.Equal(ExpenseWriteStatus.CategoryInvalid, result.Status);
    }

    // ---- Delete: hard cascade + surviving audit ----------------------------------------------------

    [SkippableFact]
    public async Task DeleteAsync_HardRemovesExpenseAndCascadesSharesAndTagsButKeepsAudit()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var tag = await SeedTagAsync(ledger.User.Id, "Công tác");
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(
            tagUuids: [tag.Uuid],
            shares: [new CreateShareData(ledger.OwnerRep.Uuid, 10_000m, null), new CreateShareData(friend.Uuid, 20_000m, null)]));
        var expenseId = created.Entity!.Id;
        var expenseUuid = created.Entity.Uuid;

        var status = await CreateExpenseRepository().DeleteAsync(ledger.User.Uuid, expenseUuid);

        Assert.Equal(ExpenseWriteStatus.Success, status);
        await using var context = CreateContext();
        Assert.Equal(0, await context.Expenses.CountAsync(expense => expense.Id == expenseId)); // hard-deleted
        Assert.Equal(0, await context.Shares.CountAsync(share => share.ExpenseId == expenseId)); // cascade
        Assert.Equal(0, await context.ExpenseTags.CountAsync(link => link.ExpenseId == expenseId)); // cascade
        // Audit survives (§3.8): create rows (1+2) + delete rows (1+2) = 6.
        var audit = await context.AuditLogs.AsNoTracking().Where(log => log.ExpenseUuid == expenseUuid).ToListAsync();
        Assert.Equal(6, audit.Count);
        Assert.Equal(3, audit.Count(log => log.Action == AuditAction.Delete));
    }

    // ---- Settled: no audit -------------------------------------------------------------------------

    [SkippableFact]
    public async Task SetSettledAsync_TogglesFlagAndWritesNoAudit()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());
        var beforeAuditCount = await CountAuditAsync(ledger.User.Id);

        var status = await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, created.Entity!.Uuid, true);

        Assert.Equal(ExpenseWriteStatus.Success, status);
        var persisted = await ReloadExpenseAsync(created.Entity.Uuid);
        Assert.True(persisted!.IsSettled);
        Assert.Equal(beforeAuditCount, await CountAuditAsync(ledger.User.Id)); // no audit for settled (OQ11)
    }

    // ---- Money: CHECK + derived total --------------------------------------------------------------

    [SkippableFact]
    public async Task Shares_NegativeAmountInsert_RejectedByDbCheckConstraint()
    {
        var ledger = await SeedLedgerAsync();
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData());

        await using var context = CreateContext();
        context.Shares.Add(new Share { ExpenseId = created.Entity!.Id, MemberId = ledger.OwnerRep.Id, Amount = -1m });

        // ck_shares_amount_non_negative rejects the negative amount at the DB (§4.3).
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task CreateAsync_ZeroAmountShare_IsAccepted()
    {
        var ledger = await SeedLedgerAsync();

        var result = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid,
            CreateData(shares: [new CreateShareData(ledger.OwnerRep.Uuid, 0m, null)]));

        Assert.Equal(ExpenseWriteStatus.Success, result.Status); // 0đ valid (§4.3)
    }

    [SkippableFact]
    public async Task GetByUuidAsync_LoadsSharesSummingToTheDerivedTotal()
    {
        var ledger = await SeedLedgerAsync();
        var friend = await SeedMemberAsync(ledger.User.Id, "An");
        var created = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(shares:
        [
            new CreateShareData(ledger.OwnerRep.Uuid, 60_000m, null),
            new CreateShareData(friend.Uuid, 40_000m, null)
        ]));

        var loaded = await CreateExpenseRepository().GetByUuidAsync(ledger.User.Uuid, created.Entity!.Uuid);

        Assert.Equal(100_000m, loaded!.Shares.Sum(share => share.Amount)); // total = sum(shares) (OQ1)
    }

    // ---- List filters + sort -----------------------------------------------------------------------

    [SkippableFact]
    public async Task ListByUserAsync_SortsByExpenseTimeDescending()
    {
        var ledger = await SeedLedgerAsync();
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Older", expenseTime: Noon.AddDays(-2)));
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Newer", expenseTime: Noon));
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Middle", expenseTime: Noon.AddDays(-1)));

        var list = await CreateExpenseRepository().ListByUserAsync(ledger.User.Uuid, new());

        Assert.Equal(["Newer", "Middle", "Older"], list.Select(expense => expense.Name)); // expense_time DESC (OQ13)
    }

    [SkippableFact]
    public async Task ListByUserAsync_FromToInclusiveRange_FiltersCorrectly()
    {
        var ledger = await SeedLedgerAsync();
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Before", expenseTime: Noon.AddDays(-5)));
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "OnFrom", expenseTime: Noon.AddDays(-2)));
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "OnTo", expenseTime: Noon));

        var list = await CreateExpenseRepository().ListByUserAsync(ledger.User.Uuid,
            new() { From = Noon.AddDays(-2), To = Noon });

        Assert.Equal(["OnTo", "OnFrom"], list.Select(expense => expense.Name)); // inclusive [from, to], DESC
    }

    [SkippableFact]
    public async Task ListByUserAsync_CategoryTagSettledFilters_CombineWithAnd()
    {
        var ledger = await SeedLedgerAsync();
        var otherCategory = await SeedCategoryAsync(ledger.User.Id, "Đi lại");
        var tag = await SeedTagAsync(ledger.User.Id, "Công tác");

        // Matches all three: default category, the tag, and settled.
        var match = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "Match",
            categoryUuid: ledger.DefaultCategory.Uuid, tagUuids: [tag.Uuid]));
        await CreateExpenseRepository().SetSettledAsync(ledger.User.Uuid, match.Entity!.Uuid, true);

        // Fails the category filter.
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "WrongCategory",
            categoryUuid: otherCategory.Uuid, tagUuids: [tag.Uuid]));
        // Fails the settled filter.
        await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, CreateData(name: "NotSettled",
            categoryUuid: ledger.DefaultCategory.Uuid, tagUuids: [tag.Uuid]));

        var list = await CreateExpenseRepository().ListByUserAsync(ledger.User.Uuid, new()
        {
            CategoryUuid = ledger.DefaultCategory.Uuid,
            TagUuid = tag.Uuid,
            Settled = true
        });

        Assert.Equal(["Match"], list.Select(expense => expense.Name)); // AND-combined (OQ13)
    }
}
