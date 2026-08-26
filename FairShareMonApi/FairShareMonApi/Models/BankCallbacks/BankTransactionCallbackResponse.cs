namespace FairShareMonApi.Models.BankCallbacks;

/// <summary>
/// One row of the owner-facing bank-callback review list (OQ5, <c>GET api/v1/bank-callbacks</c>): why an
/// expected auto-settle did (or did not) happen. Deliberately excludes the raw webhook payload (OQ7) -
/// server-side only.
/// </summary>
public sealed class BankTransactionCallbackResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string ProviderKey { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>The <c>BankCallbackOutcome</c> enum name (mirrors the <c>Tier</c>/<c>EventSettlementStatus</c> string-enum precedent - no <c>JsonStringEnumConverter</c> registered).</summary>
    public string Outcome { get; set; } = string.Empty;

    public DateTime TransactionAt { get; set; }

    public DateTime? AppliedAt { get; set; }

    /// <summary>"Share" or "EventMember", when a target was resolved (matches <c>CorrelationTargetKind</c>'s name); null otherwise.</summary>
    public string? MatchedTargetType { get; set; }

    public string? MatchedExpenseUuid { get; set; }

    public string? MatchedEventUuid { get; set; }

    public string? MatchedMemberUuid { get; set; }

    public string? MemberName { get; set; }

    public DateTime CreatedAt { get; set; }
}
