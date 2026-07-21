namespace FairShareMonApi.Constants;

/// <summary>
/// Stable integer error codes returned in the <c>ApiResult</c> envelope. Values are part of the
/// public API contract - never renumber existing codes, only append new ones. The 1xxx block is
/// reserved for cross-cutting infrastructure codes; feature areas claim their own blocks in their
/// planning docs.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Unexpected server error (HTTP 500).</summary>
    public const int InternalError = 1000;

    /// <summary>Request payload failed validation (HTTP 400).</summary>
    public const int ValidationFailed = 1001;

    /// <summary>Missing, invalid, or expired credentials (HTTP 401).</summary>
    public const int Unauthorized = 1002;

    /// <summary>Resource not found - also used for ownership misses (HTTP 404, never 403).</summary>
    public const int NotFound = 1003;

    /// <summary>
    /// Authenticated but not allowed by an authorization policy (HTTP 403). Only genuine policy
    /// failures - ownership misses always use <see cref="NotFound"/> (404, never 403).
    /// </summary>
    public const int Forbidden = 1004;

    // 2xxx - Auth (block claimed by planning/user-authentication.md).

    /// <summary>Registration username already exists (HTTP 400).</summary>
    public const int UsernameTaken = 2000;

    /// <summary>Login failed - unknown username or wrong password (HTTP 401).</summary>
    public const int InvalidCredentials = 2001;

    /// <summary>Refresh token unknown, expired, revoked, or not a refresh token (HTTP 401).</summary>
    public const int InvalidRefreshToken = 2002;

    /// <summary>Change-password rejected - current password incorrect (HTTP 400).</summary>
    public const int CurrentPasswordIncorrect = 2003;

    // 3xxx - Members (block claimed by planning/members.md).

    /// <summary>Member not found - also used for every resource-owned ownership miss (HTTP 404, never 403).</summary>
    public const int MemberNotFound = 3000;

    /// <summary>Attempt to delete the owner-representative member, which must always exist (HTTP 400).</summary>
    public const int OwnerRepresentativeNotDeletable = 3001;

    // 4xxx - Categories (block claimed by planning/categories-and-tags.md).

    /// <summary>Category not found - also used for every resource-owned ownership miss (HTTP 404, never 403).</summary>
    public const int CategoryNotFound = 4000;

    /// <summary>A category with the same active name already exists in the ledger (HTTP 400).</summary>
    public const int CategoryNameDuplicate = 4001;

    /// <summary>Attempt to delete the default category, which must always exist (HTTP 400).</summary>
    public const int DefaultCategoryNotDeletable = 4002;

    // 5xxx - Tags (block claimed by planning/categories-and-tags.md).

    /// <summary>Tag not found - also used for every resource-owned ownership miss (HTTP 404, never 403).</summary>
    public const int TagNotFound = 5000;

    /// <summary>A tag with the same active name already exists in the ledger (HTTP 400).</summary>
    public const int TagNameDuplicate = 5001;

    // 6xxx - Expenses (block claimed by planning/expenses-shares-audit.md).

    /// <summary>Expense not found - also used for every resource-owned ownership miss (HTTP 404, never 403).</summary>
    public const int ExpenseNotFound = 6000;

    /// <summary>The chosen payer member is foreign or soft-deleted, so not selectable (§4.2/§4.8, HTTP 400).</summary>
    public const int ExpensePayerInvalid = 6001;

    /// <summary>The chosen category is foreign or soft-deleted, so not selectable (§4.2/§4.8, HTTP 400).</summary>
    public const int ExpenseCategoryInvalid = 6002;

    /// <summary>A chosen tag is foreign or soft-deleted, so not selectable (§4.2/§4.8, HTTP 400).</summary>
    public const int ExpenseTagInvalid = 6003;

    // 7xxx - Shares (block claimed by planning/expenses-shares-audit.md).

    /// <summary>Share not found - also used for every resource-owned ownership miss (HTTP 404, never 403).</summary>
    public const int ShareNotFound = 7000;

    /// <summary>A share's member is foreign or soft-deleted, so not selectable (§4.2/§4.8, HTTP 400).</summary>
    public const int ShareMemberInvalid = 7001;

    /// <summary>Attempt to delete the owner-representative member's share, which must always exist (§5, HTTP 400).</summary>
    public const int OwnerRepresentativeShareNotDeletable = 7002;

    /// <summary>Two shares for the same member in one expense - forbidden (OQ5, HTTP 400).</summary>
    public const int DuplicateShareMember = 7003;

    // 8xxx - Audit (reserved by planning/expenses-shares-audit.md; the history read reuses
    // ExpenseNotFound/empty-list semantics, so no codes are needed yet).

    // 9xxx - Events (block claimed by planning/events.md).

    /// <summary>Event not found - also used for every resource-owned ownership miss (HTTP 404, never 403).</summary>
    public const int EventNotFound = 9000;

    /// <summary>The event is closed: every write to its expenses/shares is rejected except the settled flag (§4.4, HTTP 400). Also covers editing/deleting/re-closing a closed event.</summary>
    public const int EventClosed = 9001;

    /// <summary>The expense's expense_time is outside the event's date range (on assign, create-into-event, or expense_time edit) (HTTP 400).</summary>
    public const int ExpenseTimeOutOfEventRange = 9002;

    /// <summary>Editing the event's range would leave an already-assigned expense out of range (OQ7, HTTP 400).</summary>
    public const int EventRangeExcludesAssignedExpenses = 9003;

    // 10xxx - Stats (block reserved by planning/debt-balance-and-stats.md; M7 is read-only and needs no
    // new codes - a resource-owned event miss (balance / by-category?eventUuid) reuses EventNotFound
    // (9000) and a bad time range / both-scopes request is a ValidationFailed (1001, error.fields). No
    // codes are defined yet (OQ13a); this block is reserved for any future Stats-specific failure state.

    // 11xxx - Export (block reserved by planning/export-csv.md; M8 is read-only and needs no new codes -
    // an unsupported ?format value is a ValidationFailed (1001) and a resource-owned expense/event miss
    // reuses ExpenseNotFound (6000) / EventNotFound (9000). No codes are defined yet (OQ19a); this block
    // is reserved for any future Export-specific failure state.

    // 12xxx - Wallet / QR (block claimed by planning/wallet-and-qr.md).

    /// <summary>Bank account not found - also used for every resource-owned ownership miss (HTTP 404, never 403).</summary>
    public const int BankAccountNotFound = 12000;

    /// <summary>No bank account (or no default and no override) to generate a QR against (HTTP 400).</summary>
    public const int NoBankAccountForQr = 12001;

    /// <summary>Event QR requested on an event that is not closed - only closed events may generate the event QR (§4.4, HTTP 400).</summary>
    public const int EventNotClosedForQr = 12002;

    /// <summary>Event QR requested but no member still owes (all balances ≥ 0) - nothing to bill (HTTP 400).</summary>
    public const int NoOutstandingDebtForQr = 12003;

    // 13xxx - Tiers (block claimed by planning/tiers-premium-free.md; M11 Admin takes the next free block, 14xxx).

    /// <summary>Free tier reached its active-member cap; a new member is rejected (HTTP 400). Premium removes the limit.</summary>
    public const int MemberLimitReached = 13000;

    /// <summary>Free tier reached its open-event cap; a new event is rejected (HTTP 400). Close an event or upgrade to Premium.</summary>
    public const int OpenEventLimitReached = 13001;

    /// <summary>Free tier reached its expenses-per-month cap; a new expense this month is rejected (HTTP 400). Premium removes the limit.</summary>
    public const int MonthlyExpenseLimitReached = 13002;

    /// <summary>A Premium-only ("mở rộng") feature was used by a Free account (HTTP 403, distinct from generic Forbidden 1004 so clients can show an upsell).</summary>
    public const int PremiumFeatureRequired = 13003;

    // 14xxx - Admin (block claimed by planning/admin-management.md, M11).

    /// <summary>Admin target user not found by uuid (HTTP 404).</summary>
    public const int AdminUserNotFound = 14000;

    /// <summary>Admin attempted a destructive action on their own account (disable/demote/revoke-tokens/reset-password) (HTTP 400).</summary>
    public const int AdminCannotTargetSelf = 14001;

    /// <summary>Admin attempted a destructive action on another admin, or one that would leave the system with zero admins (HTTP 400).</summary>
    public const int AdminCannotTargetAdmin = 14002;

    /// <summary>Login rejected because the account is disabled (HTTP 403).</summary>
    public const int AccountDisabled = 14003;

    // 15xxx - Settled per member (block reserved by planning/settled-per-member.md; the feature is
    // resource-owned only and needs no new codes - every failure reuses an existing miss code:
    // ShareNotFound (7000) / ExpenseNotFound (6000) / EventNotFound (9000) / MemberNotFound (3000, also
    // the non-participant case, settled-per-member OQ12a). No codes are defined yet; this block is
    // reserved for any future settled-per-member-specific failure state.
}
