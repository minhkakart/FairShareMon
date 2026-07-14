using System.Text.Json.Serialization;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FairShareMonApi.Models;

/// <summary>Error payload of the <see cref="ApiResult"/> envelope.</summary>
public class ApiError
{
    public required int Code { get; init; }

    public required string Message { get; init; }

    /// <summary>Per-field validation errors (field name -> messages); omitted when not a validation error.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Fields { get; init; }
}

/// <summary>
/// Uniform response envelope: <c>{ data, isSuccess, error: { code, message } }</c>. Implements
/// <see cref="IActionResult"/> so controllers return it directly; the HTTP status is derived from
/// the attached error (400/401/404/500) rather than being always-200.
/// </summary>
public class ApiResult : IActionResult
{
    public object? Data { get; protected init; }

    public bool IsSuccess { get; protected init; }

    public ApiError? Error { get; protected init; }

    [JsonIgnore]
    public int StatusCode { get; init; } = StatusCodes.Status200OK;

    public static ApiResult SuccessMessage(string message) => new()
    {
        IsSuccess = true,
        Data = new { Message = message }
    };

    public static ApiResult Failure(int code, string message, IReadOnlyDictionary<string, string[]>? fields = null, int? statusCode = null) => new()
    {
        IsSuccess = false,
        Error = new ApiError { Code = code, Message = message, Fields = fields },
        StatusCode = statusCode ?? ErrorException.GetDefaultHttpStatus(code)
    };

    public static ApiResult Failure(ErrorException exception) =>
        Failure(exception.Code, exception.Message, statusCode: exception.HttpStatus);

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var objectResult = new ObjectResult(this) { StatusCode = StatusCode };
        await objectResult.ExecuteResultAsync(context);
    }
}

public class ApiResult<T> : ApiResult
{
    public static ApiResult<T> Success(T data, int statusCode = StatusCodes.Status200OK) => new()
    {
        IsSuccess = true,
        Data = data,
        StatusCode = statusCode
    };
}
