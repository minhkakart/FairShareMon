namespace FairShareMonApi.Repositories.Stats;

/// <summary>
/// Repository-facing aggregate result records for the read-only Stats/Balance queries (M7). These are
/// NOT DTOs - they carry the DB-side <c>GROUP BY</c>/<c>SUM</c>/<c>COUNT</c> results (plus the joined
/// display fields) up to <c>StatsService</c>, which maps them to the <c>Models/Stats</c> responses.
/// </summary>

/// <summary>
/// One participating member's advanced/owed figures in an event's balance (§3.7). Both figures are
/// resolved from the SAME single share-set (the event's expenses' shares) - advanced grouped by
/// <c>expense.payer_member_id</c>, owed grouped by <c>share.member_id</c> - so the balances sum to zero
/// by construction (OQ1). Member display fields are denormalized so a soft-deleted member still shows
/// (OQ3/§4.7). Balance = <c>Advanced - Owed</c> is derived at map time.
/// </summary>
/// <remarks>
/// <see cref="IsSettled"/> / <see cref="SettledAt"/> are the Layer B per-member-per-event net-clearance
/// flags (settled-per-member OQ1a/OQ8a), loaded additively from <c>event_member_settlements</c> - they do
/// NOT change <see cref="Advanced"/>/<see cref="Owed"/>/balance (D2 / M7 OQ2 preserved). Default
/// false/null for a participant with no settlement row. The derived <c>outstanding</c> overlay is computed
/// from these + balance in <c>StatsService</c>, not stored here.
/// </remarks>
/// <param name="IsEligibleForAutoCascade">
/// Event-expense-settlement-sync Step M1.4 (OQ4): whether Direction 1's auto-cascade would fire for this
/// member (a net debtor, or a gross-pure net creditor), derived from the same canonical
/// <c>EventSettlementClassifier</c> helper Direction 1 itself gates on.
/// </param>
/// <param name="ClearedAmount">
/// Event-expense-settlement-sync Step M2.5: cumulative amount credited toward this member's net owed debt
/// (Direction 1's manual snapshot and/or Direction 2's per-share/whole-expense credits) - the sole source
/// of truth <c>Outstanding</c>/<c>SettlementStatus</c> derive from in <c>StatsService</c> (OQ2). Default
/// <c>0</c> for a participant with no settlement row.
/// </param>
public sealed record MemberBalanceAggregate(
    string MemberUuid,
    string MemberName,
    bool IsOwnerRepresentative,
    bool IsDeleted,
    decimal Advanced,
    decimal Owed,
    bool IsSettled,
    DateTime? SettledAt,
    bool IsEligibleForAutoCascade,
    decimal ClearedAmount);

/// <summary>Overview totals over the owner's whole ledger in a time range (OQ6): total spending (= sum of shares) and the distinct expense count.</summary>
public sealed record OverviewAggregate(
    decimal TotalSpending,
    int ExpenseCount);

/// <summary>
/// One category's spend total + expense count in scope (§3.9). Only categories with at least one
/// in-scope expense are produced; soft-deleted categories with historical expenses are included
/// (OQ9/§4.7), flagged <see cref="IsDeleted"/> and carrying their color/icon.
/// </summary>
public sealed record CategoryStatAggregate(
    string CategoryUuid,
    string CategoryName,
    string Color,
    string? Icon,
    bool IsDeleted,
    decimal Total,
    int ExpenseCount);
