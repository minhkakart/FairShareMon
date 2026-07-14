using FairShareMonApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FairShareMonApi.Attributes;

/// <summary>
/// Marks endpoints whose responses use the <see cref="ApiResult"/> envelope. Doubles as
/// (1) endpoint metadata consumed by <c>ErrorHandlerMiddleware</c> to decide whether an unhandled
/// exception is wrapped, and (2) a result filter that wraps plain object returns into
/// <see cref="ApiResult{T}"/> so actions may return DTOs directly. Only 2xx object results are
/// auto-wrapped - non-success statuses (e.g. <c>BadRequest(dto)</c>) pass through untouched so
/// they are never mislabeled <c>isSuccess: true</c>; error responses should go through
/// <c>ApiResult.Failure</c> / <c>ErrorException</c> instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ResponseWrappedAttribute : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult objectResult || objectResult.Value is ApiResult)
            return;

        var statusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;
        if (statusCode < StatusCodes.Status200OK || statusCode >= StatusCodes.Status300MultipleChoices)
            return;

        context.Result = ApiResult<object?>.Success(objectResult.Value, statusCode);
    }
}
