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
}
