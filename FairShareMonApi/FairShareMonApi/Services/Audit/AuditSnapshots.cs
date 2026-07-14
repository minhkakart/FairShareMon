using FairShareMonApi.Database.Entities;

namespace FairShareMonApi.Services.Audit;

/// <summary>
/// Denormalized snapshot of an expense for the audit log (OQ10): carries display names (payer /
/// category / tag) alongside uuids so the history stays readable after those are renamed or deleted.
/// Tags are ordered by uuid so the same tag set always serializes identically (no-op detection, OQ9).
/// </summary>
public sealed record ExpenseAuditSnapshot(
    string Uuid,
    string Name,
    string? Description,
    DateTime ExpenseTime,
    string PayerMemberUuid,
    string PayerMemberName,
    string CategoryUuid,
    string CategoryName,
    IReadOnlyList<AuditTagRef> Tags,
    bool IsSettled)
{
    public static ExpenseAuditSnapshot From(Expense expense, Member payer, Category category, IReadOnlyList<Tag> tags) =>
        new(
            expense.Uuid,
            expense.Name,
            expense.Description,
            AuditSnapshotCanonicalizer.Utc(expense.ExpenseTime),
            payer.Uuid,
            payer.Name,
            category.Uuid,
            category.Name,
            tags.OrderBy(tag => tag.Uuid).Select(tag => new AuditTagRef(tag.Uuid, tag.Name)).ToList(),
            expense.IsSettled);
}

/// <summary>A tag reference in an audit snapshot (uuid + denormalized name).</summary>
public sealed record AuditTagRef(string Uuid, string Name);

/// <summary>
/// Denormalized snapshot of a share for the audit log (OQ10): carries the member's display name
/// alongside the uuids so the history stays readable after the member is renamed or deleted.
/// </summary>
public sealed record ShareAuditSnapshot(
    string Uuid,
    string ExpenseUuid,
    string MemberUuid,
    string MemberName,
    decimal Amount,
    string? Note)
{
    public static ShareAuditSnapshot From(Share share, string expenseUuid, Member member) =>
        new(share.Uuid, expenseUuid, member.Uuid, member.Name, AuditSnapshotCanonicalizer.Money(share.Amount), share.Note);
}

/// <summary>
/// Canonicalizes snapshot values so semantically-equal before/after states serialize identically,
/// making no-op detection (OQ9) robust against representation drift on the DB round-trip:
/// a <see cref="DateTime"/> loses its <c>Kind</c> (stored UTC comes back <c>Unspecified</c>) and a
/// <c>DECIMAL(18,2)</c> amount comes back at scale 2 while a client value may be scale 0.
/// </summary>
public static class AuditSnapshotCanonicalizer
{
    /// <summary>
    /// Labels a timestamp as UTC without shifting the clock value (the codebase stores UTC via
    /// <c>AppDateTime.Now</c>), so a <c>Kind.Utc</c> request value and its <c>Kind.Unspecified</c>
    /// DB round-trip serialize the same (both with a trailing <c>Z</c>).
    /// </summary>
    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>
    /// Normalizes a money value to the column's fixed scale of 2, so <c>40000</c> and <c>40000.00</c>
    /// serialize identically (adding <c>0.00m</c> forces scale 2 regardless of the input scale).
    /// </summary>
    public static decimal Money(decimal value) => decimal.Round(value, 2) + 0.00m;
}
