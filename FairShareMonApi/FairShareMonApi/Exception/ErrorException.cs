using FairShareMonApi.Constants;

namespace FairShareMonApi.Exceptions;

/// <summary>
/// Application error carrying a stable error code and the HTTP status the <c>ApiResult</c>
/// envelope should respond with. The folder is <c>Exception/</c> per conventions, but the
/// namespace is <c>Exceptions</c> so the simple name <c>Exception</c> keeps resolving to
/// <see cref="System.Exception"/> everywhere in the codebase.
/// <para>
/// The message is carried as a <b>resource key</b> (<see cref="MessageKey"/>) plus optional
/// <see cref="Args"/> and is localized once at the envelope boundary
/// (<c>ErrorHandlerFilter</c> / <c>ErrorHandlerMiddleware</c>) via <c>IStringLocalizer</c>, not at the
/// throw site (planning/localization-subsystem.md D2). The base <see cref="System.Exception.Message"/>
/// holds the key so logs record a stable, culture-independent identifier.
/// </para>
/// </summary>
public class ErrorException(int code, string messageKey, int? httpStatus = null, object[]? args = null)
    : System.Exception(messageKey)
{
    public int Code { get; } = code;

    /// <summary>Resource key resolved against the <c>StringResources</c> resx family at the envelope.</summary>
    public string MessageKey { get; } = messageKey;

    /// <summary>Optional <c>string.Format</c> arguments for an interpolated (<c>{0}</c>...) message.</summary>
    public object[]? Args { get; } = args;

    public int HttpStatus { get; } = httpStatus ?? GetDefaultHttpStatus(code);

    /// <summary>Maps a stable error code to its default HTTP status.</summary>
    public static int GetDefaultHttpStatus(int code) => code switch
    {
        ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.UsernameTaken => StatusCodes.Status400BadRequest,
        ErrorCodes.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ErrorCodes.InvalidRefreshToken => StatusCodes.Status401Unauthorized,
        ErrorCodes.CurrentPasswordIncorrect => StatusCodes.Status400BadRequest,
        ErrorCodes.MemberNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.OwnerRepresentativeNotDeletable => StatusCodes.Status400BadRequest,
        ErrorCodes.CategoryNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.CategoryNameDuplicate => StatusCodes.Status400BadRequest,
        ErrorCodes.DefaultCategoryNotDeletable => StatusCodes.Status400BadRequest,
        ErrorCodes.TagNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.TagNameDuplicate => StatusCodes.Status400BadRequest,
        ErrorCodes.ExpenseNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.ExpensePayerInvalid => StatusCodes.Status400BadRequest,
        ErrorCodes.ExpenseCategoryInvalid => StatusCodes.Status400BadRequest,
        ErrorCodes.ExpenseTagInvalid => StatusCodes.Status400BadRequest,
        ErrorCodes.ShareNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.ShareMemberInvalid => StatusCodes.Status400BadRequest,
        ErrorCodes.OwnerRepresentativeShareNotDeletable => StatusCodes.Status400BadRequest,
        ErrorCodes.DuplicateShareMember => StatusCodes.Status400BadRequest,
        ErrorCodes.EventNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.EventClosed => StatusCodes.Status400BadRequest,
        ErrorCodes.ExpenseTimeOutOfEventRange => StatusCodes.Status400BadRequest,
        ErrorCodes.EventRangeExcludesAssignedExpenses => StatusCodes.Status400BadRequest,
        ErrorCodes.BankAccountNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.NoBankAccountForQr => StatusCodes.Status400BadRequest,
        ErrorCodes.EventNotClosedForQr => StatusCodes.Status400BadRequest,
        ErrorCodes.NoOutstandingDebtForQr => StatusCodes.Status400BadRequest,
        ErrorCodes.MemberLimitReached => StatusCodes.Status400BadRequest,
        ErrorCodes.OpenEventLimitReached => StatusCodes.Status400BadRequest,
        ErrorCodes.MonthlyExpenseLimitReached => StatusCodes.Status400BadRequest,
        ErrorCodes.PremiumFeatureRequired => StatusCodes.Status403Forbidden,
        ErrorCodes.AdminUserNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.AdminCannotTargetSelf => StatusCodes.Status400BadRequest,
        ErrorCodes.AdminCannotTargetAdmin => StatusCodes.Status400BadRequest,
        ErrorCodes.AccountDisabled => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError
    };
}
