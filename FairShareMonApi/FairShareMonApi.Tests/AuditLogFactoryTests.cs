using System.Text.Json;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Services.Audit;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="AuditLogFactory"/> (OQ9/OQ10/OQ20) - no DB. Proves: Create builds a
/// row with <c>before = null</c> and a denormalized after-snapshot; Delete builds a row with
/// <c>after = null</c> carrying the before-snapshot; an Update with a real change logs both states; a
/// <b>no-op Update returns null</b> (no row, OQ9); snapshots embed denormalized display names alongside
/// uuids so history stays readable after renames/deletes (OQ10); expense rows carry
/// <c>expense_uuid == entity_uuid</c> and share rows carry the owning expense's uuid.
/// </summary>
public class AuditLogFactoryTests
{
    private const ulong ActorUserId = 42;

    private readonly AuditLogFactory _factory = new();

    private static Member Member(string name = "An", bool ownerRep = false) =>
        new() { Name = name, IsOwnerRepresentative = ownerRep };

    private static Category Category(string name = "Ăn uống") =>
        new() { Name = name, Color = "#F97316" };

    private static Expense Expense(string name = "Ăn trưa", bool settled = false) =>
        new() { Name = name, ExpenseTime = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc), IsSettled = settled };

    private static Share Share(decimal amount = 100_000m, string? note = null) =>
        new() { Amount = amount, Note = note };

    private static ExpenseAuditSnapshot ExpenseSnapshot(Expense expense, Member payer, Category category, IReadOnlyList<Tag>? tags = null) =>
        ExpenseAuditSnapshot.From(expense, payer, category, tags ?? []);

    private static ShareAuditSnapshot ShareSnapshot(Share share, string expenseUuid, Member member) =>
        ShareAuditSnapshot.From(share, expenseUuid, member);

    [Fact]
    public void BuildExpenseAudit_Create_HasNullBeforeAndDenormalizedAfter()
    {
        var expense = Expense("Ăn trưa");
        var payer = Member("Chủ sổ", ownerRep: true);
        var category = Category("Ăn uống");
        var tag = new Tag { Name = "Công tác" };

        var log = _factory.BuildExpenseAudit(AuditAction.Create, before: null, after: ExpenseSnapshot(expense, payer, category, [tag]), ActorUserId);

        Assert.NotNull(log);
        Assert.Equal(AuditEntityType.Expense, log!.EntityType);
        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal(ActorUserId, log.ActorUserId);
        Assert.Null(log.BeforeData);
        Assert.NotNull(log.AfterData);
        Assert.Equal(expense.Uuid, log.EntityUuid);
        Assert.Equal(expense.Uuid, log.ExpenseUuid); // expense rows: expense_uuid == entity_uuid

        using var after = JsonDocument.Parse(log.AfterData!);
        var root = after.RootElement;
        Assert.Equal("Ăn trưa", root.GetProperty("name").GetString());
        Assert.Equal("Chủ sổ", root.GetProperty("payerMemberName").GetString()); // denormalized name (OQ10)
        Assert.Equal("Ăn uống", root.GetProperty("categoryName").GetString());
        Assert.Equal("Công tác", root.GetProperty("tags")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void BuildExpenseAudit_Delete_HasNullAfterAndBeforeSnapshot()
    {
        var expense = Expense();
        var payer = Member();
        var category = Category();

        var log = _factory.BuildExpenseAudit(AuditAction.Delete, before: ExpenseSnapshot(expense, payer, category), after: null, ActorUserId);

        Assert.NotNull(log);
        Assert.Equal(AuditAction.Delete, log!.Action);
        Assert.NotNull(log.BeforeData);
        Assert.Null(log.AfterData);
        Assert.Equal(expense.Uuid, log.EntityUuid);
    }

    [Fact]
    public void BuildExpenseAudit_UpdateWithRealChange_LogsBeforeAndAfter()
    {
        var expense = Expense("Ăn trưa");
        var payer = Member();
        var category = Category();
        var before = ExpenseSnapshot(expense, payer, category);

        expense.Name = "Ăn tối"; // a real change
        var after = ExpenseSnapshot(expense, payer, category);

        var log = _factory.BuildExpenseAudit(AuditAction.Update, before, after, ActorUserId);

        Assert.NotNull(log);
        Assert.Equal(AuditAction.Update, log!.Action);
        Assert.NotNull(log.BeforeData);
        Assert.NotNull(log.AfterData);
        // Parse rather than substring-match: System.Text.Json escapes non-ASCII (Vietnamese) in the raw JSON.
        using var before2 = JsonDocument.Parse(log.BeforeData!);
        using var after2 = JsonDocument.Parse(log.AfterData!);
        Assert.Equal("Ăn trưa", before2.RootElement.GetProperty("name").GetString());
        Assert.Equal("Ăn tối", after2.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void BuildExpenseAudit_NoOpUpdate_ReturnsNull()
    {
        var expense = Expense("Ăn trưa");
        var payer = Member();
        var category = Category();

        var before = ExpenseSnapshot(expense, payer, category);
        var after = ExpenseSnapshot(expense, payer, category); // identical

        var log = _factory.BuildExpenseAudit(AuditAction.Update, before, after, ActorUserId);

        Assert.Null(log); // no-op edit produces no row (OQ9)
    }

    [Fact]
    public void BuildExpenseAudit_UpdateWithReorderedSameTags_ReturnsNull()
    {
        var expense = Expense();
        var payer = Member();
        var category = Category();
        var tagA = new Tag { Name = "A" };
        var tagB = new Tag { Name = "B" };

        // Tags are ordered by uuid inside the snapshot, so the same set in a different order is a no-op.
        var before = ExpenseSnapshot(expense, payer, category, [tagA, tagB]);
        var after = ExpenseSnapshot(expense, payer, category, [tagB, tagA]);

        var log = _factory.BuildExpenseAudit(AuditAction.Update, before, after, ActorUserId);

        Assert.Null(log);
    }

    [Fact]
    public void BuildShareAudit_Create_HasNullBeforeDenormalizedMemberNameAndOwningExpenseUuid()
    {
        var expense = Expense();
        var member = Member("Bình");
        var share = Share(250_000m, "Trả trước");

        var log = _factory.BuildShareAudit(AuditAction.Create, before: null, after: ShareSnapshot(share, expense.Uuid, member), ActorUserId);

        Assert.NotNull(log);
        Assert.Equal(AuditEntityType.Share, log!.EntityType);
        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Null(log.BeforeData);
        Assert.Equal(share.Uuid, log.EntityUuid);
        Assert.Equal(expense.Uuid, log.ExpenseUuid); // groups the share under its expense

        using var after = JsonDocument.Parse(log.AfterData!);
        var root = after.RootElement;
        Assert.Equal("Bình", root.GetProperty("memberName").GetString()); // denormalized (OQ10)
        Assert.Equal(250_000m, root.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public void BuildShareAudit_Delete_HasNullAfterAndBeforeSnapshot()
    {
        var expense = Expense();
        var member = Member();
        var share = Share();

        var log = _factory.BuildShareAudit(AuditAction.Delete, before: ShareSnapshot(share, expense.Uuid, member), after: null, ActorUserId);

        Assert.NotNull(log);
        Assert.Equal(AuditAction.Delete, log!.Action);
        Assert.NotNull(log.BeforeData);
        Assert.Null(log.AfterData);
    }

    [Fact]
    public void BuildShareAudit_NoOpUpdate_ReturnsNull()
    {
        var expense = Expense();
        var member = Member();
        var share = Share(100_000m);

        var before = ShareSnapshot(share, expense.Uuid, member);
        var after = ShareSnapshot(share, expense.Uuid, member);

        var log = _factory.BuildShareAudit(AuditAction.Update, before, after, ActorUserId);

        Assert.Null(log);
    }

    [Fact]
    public void BuildShareAudit_UpdateAmountChange_LogsBeforeAndAfter()
    {
        var expense = Expense();
        var member = Member();
        var share = Share(100_000m);
        var before = ShareSnapshot(share, expense.Uuid, member);

        share.Amount = 200_000m;
        var after = ShareSnapshot(share, expense.Uuid, member);

        var log = _factory.BuildShareAudit(AuditAction.Update, before, after, ActorUserId);

        Assert.NotNull(log);
        Assert.NotNull(log!.BeforeData);
        Assert.NotNull(log.AfterData);
    }
}
