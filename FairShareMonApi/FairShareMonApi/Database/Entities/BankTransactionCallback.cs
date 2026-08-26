using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Immutable record of every inbound bank-transaction webhook, matched or not (table
/// <c>bank_transaction_callbacks</c>, planning/bank-callback-settlement.md, Decision Log entry 4/6). A
/// SEPARATE table from <see cref="AuditLog"/> (which is shaped around a human <see cref="AuditLog.ActorUserId"/>
/// and before/after entity snapshots) since a webhook has no human actor and this table's shape is
/// closer to a transaction ledger. Doubles as the idempotency dedup store: the unique
/// <c>(provider_key, provider_transaction_id)</c> index is the sole backstop against a retried/
/// duplicated webhook double-applying. <see cref="RawPayload"/> is retained indefinitely (OQ7) but never
/// surfaced on the owner-facing <c>GET api/v1/bank-callbacks</c> response - server-side only.
/// </summary>
public partial class BankTransactionCallback : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>The <see cref="Services.Api.BankCallbacks.IBankCallbackParser.ProviderKey"/> that produced this row (e.g. "sepay").</summary>
    public required string ProviderKey { get; set; }

    /// <summary>The provider's own transaction id - half of the idempotency dedup key.</summary>
    public required string ProviderTransactionId { get; set; }

    /// <summary>True for an incoming transfer; false (e.g. "out") is always recorded as <see cref="BankCallbackOutcome.Ignored"/>.</summary>
    public bool IsIncoming { get; set; }

    /// <summary>The transferred VND amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>The receiving bank's BIN, when the provider's payload includes it (OQ6, soft check only).</summary>
    public string? BankBin { get; set; }

    /// <summary>The receiving account number, when the provider's payload includes it (OQ6, soft check only).</summary>
    public string? DestinationAccountNumber { get; set; }

    /// <summary>The free-text transfer memo/content the correlation code is extracted from.</summary>
    public required string Content { get; set; }

    /// <summary>The correlation code extracted from <see cref="Content"/> (or the provider's own pre-extracted field), if any.</summary>
    public string? ExtractedCode { get; set; }

    /// <summary>When the underlying bank transaction occurred (per the provider's payload).</summary>
    public DateTime TransactionAt { get; set; }

    /// <summary>The verbatim webhook body, for audit/dispute resolution. Retained indefinitely (OQ7); server-side only, never returned to the owner.</summary>
    public required string RawPayload { get; set; }

    /// <summary>The correlation code row this transaction resolved to, if any (FK -> <c>qr_correlation_codes.id</c>, set null on delete).</summary>
    public ulong? MatchedCorrelationCodeId { get; set; }

    /// <summary>The owner this transaction is visible to (FK -> <c>users.id</c>, cascade delete); null when no user could be resolved (e.g. <see cref="BankCallbackOutcome.UnmatchedCode"/> with an unknown code) - OQ5's known trade-off.</summary>
    public ulong? ResolvedUserId { get; set; }

    /// <summary>What happened when this transaction was processed.</summary>
    public BankCallbackOutcome Outcome { get; set; }

    /// <summary>Free-text diagnostic note for a held-back outcome (e.g. the caught exception message on <see cref="BankCallbackOutcome.VerificationFailed"/>).</summary>
    public string? FailureNote { get; set; }

    /// <summary>When the existing settled toggle was successfully called. Null unless <see cref="Outcome"/> is <see cref="BankCallbackOutcome.Applied"/>.</summary>
    public DateTime? AppliedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public QrCorrelationCode? MatchedCorrelationCode { get; set; }

    public User? ResolvedUser { get; set; }
}
