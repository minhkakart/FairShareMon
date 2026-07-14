namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Lightweight join row linking an <see cref="Expense"/> to a <see cref="Tag"/> (table
/// <c>expense_tags</c>, OQ6). A pure link table: composite PK <c>(expense_id, tag_id)</c>, no
/// id/uuid/timestamps/soft-delete. The expense FK cascades (tags detach when an expense is
/// hard-deleted); the tag FK restricts (tags are soft-deleted). A soft-deleted tag stays linked on
/// existing expenses (§4.7) but is not selectable for new/edited expenses (§4.8).
/// </summary>
public partial class ExpenseTag
{
    public ulong ExpenseId { get; set; }

    public ulong TagId { get; set; }

    public Expense Expense { get; set; } = null!;

    public Tag Tag { get; set; } = null!;
}
