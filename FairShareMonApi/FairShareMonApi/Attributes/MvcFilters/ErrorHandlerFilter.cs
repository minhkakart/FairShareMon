using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Extensions;
using FairShareMonApi.Localization.Resources;
using FairShareMonApi.Models;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace FairShareMonApi.Attributes.MvcFilters;

/// <summary>
/// Global MVC filter: surfaces ModelState/binding errors as 400 <see cref="ApiResult"/> responses
/// (the built-in invalid-model filter is suppressed in <c>Program.cs</c>) and maps
/// <see cref="ErrorException"/> / FluentValidation's <see cref="ValidationException"/> (thrown by
/// manual validation in services) to wrapped responses. Anything else bubbles up to
/// <c>ErrorHandlerMiddleware</c>. User-facing text is localized here (the envelope boundary) via
/// <see cref="IStringLocalizer{StringResources}"/> using the request's <c>CurrentUICulture</c>.
/// </summary>
public sealed class ErrorHandlerFilter(IStringLocalizer<StringResources> localizer) : IActionFilter, IExceptionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
            return;

        context.Result = ApiResult.Failure(
            ErrorCodes.ValidationFailed,
            localizer[MessageKeys.Envelope.ValidationFailed],
            CollectFields(context.ModelState, localizer[MessageKeys.Envelope.FieldInvalid]));
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ValidationException validationException:
                context.Result = ApiResult.Failure(
                    ErrorCodes.ValidationFailed,
                    localizer[MessageKeys.Envelope.ValidationFailed],
                    CollectFields(validationException));
                context.ExceptionHandled = true;
                return;
            case ErrorException errorException:
                context.Result = ApiResult.Failure(
                    errorException.Code,
                    localizer.LocalizeError(errorException),
                    statusCode: errorException.HttpStatus);
                context.ExceptionHandled = true;
                return;
        }
    }

    // Field keys are camelCased so error.fields matches the envelope's camelCase JSON contract.
    private static IReadOnlyDictionary<string, string[]> CollectFields(ModelStateDictionary modelState, string fieldInvalidFallback) =>
        modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key))
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(entry => entry.Value!.Errors)
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? fieldInvalidFallback : error.ErrorMessage)
                    .ToArray());

    private static IReadOnlyDictionary<string, string[]> CollectFields(ValidationException exception) =>
        exception.Errors
            .GroupBy(failure => JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
}
