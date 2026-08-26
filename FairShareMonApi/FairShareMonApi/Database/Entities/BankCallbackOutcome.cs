namespace FairShareMonApi.Database.Entities;

/// <summary>
/// The result of processing one inbound bank-transaction webhook (planning/bank-callback-settlement.md
/// Step 5). Always recorded, even when nothing was applied - a held-back transaction stays visible
/// (OQ5) instead of being silently dropped.
/// </summary>
public enum BankCallbackOutcome
{
    /// <summary>Outbound transfer (or otherwise not incoming) - never a settlement target.</summary>
    Ignored = 0,

    /// <summary>The memo carried no extractable/resolvable correlation code.</summary>
    UnmatchedCode = 1,

    /// <summary>The code resolved, but the transferred amount did not exactly match the current expected amount (OQ4).</summary>
    AmountMismatch = 2,

    /// <summary>Verification failed, or applying the settle hit a resource-owned race (Decision Log entry 6).</summary>
    VerificationFailed = 3,

    /// <summary>The resolved target was already settled - an idempotent no-op (a retried/duplicate transfer).</summary>
    AlreadySettledNoOp = 4,

    /// <summary>The existing settled toggle was called and the target is now settled.</summary>
    Applied = 5
}
