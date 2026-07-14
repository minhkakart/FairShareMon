using FairShareMonApi.Constants;

namespace FairShareMonApi.Exceptions;

/// <summary>
/// Application error carrying a stable error code and the HTTP status the <c>ApiResult</c>
/// envelope should respond with. The folder is <c>Exception/</c> per conventions, but the
/// namespace is <c>Exceptions</c> so the simple name <c>Exception</c> keeps resolving to
/// <see cref="System.Exception"/> everywhere in the codebase.
/// </summary>
public class ErrorException(int code, string message, int? httpStatus = null) : System.Exception(message)
{
    public int Code { get; } = code;

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
        _ => StatusCodes.Status500InternalServerError
    };
}
