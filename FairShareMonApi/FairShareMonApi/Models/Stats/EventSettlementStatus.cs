namespace FairShareMonApi.Models.Stats;

/// <summary>
/// Internal, service-only tri-state used purely for type-safe computation inside <c>StatsService</c>
/// (event-expense-settlement-sync Step M2.5, OQ5/OQ-WF). <b>Never the type of a DTO property</b> - no
/// <c>JsonStringEnumConverter</c> is registered in <c>Program.cs</c>, so a raw enum-typed response
/// property would serialize as an integer; <see cref="MemberBalanceRow.SettlementStatus"/> is <c>string</c>,
/// assigned via <c>.ToString()</c> on a value of this enum (Decision Log entry 6). Vietnamese copy: "chưa
/// trả" (Unsettled), "đã trả một phần" (PartiallySettled), "đã trả" (Settled).
/// </summary>
internal enum EventSettlementStatus
{
    /// <summary>Not a net debtor (<c>NetOwed &lt;= 0</c>, n/a), or a net debtor who has cleared none of it (<c>ClearedAmount == 0</c>).</summary>
    Unsettled,

    /// <summary>The member owes net and has cleared some, but not all, of it (<c>0 &lt; ClearedAmount &lt; NetOwed</c>).</summary>
    PartiallySettled,

    /// <summary>The member's net owed debt (<c>NetOwed &gt; 0</c>) has been fully cleared (<c>Outstanding &lt;= 0</c>).</summary>
    Settled
}
