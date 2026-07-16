using FairShareMonApi.Exceptions;
using Microsoft.Extensions.Localization;

namespace FairShareMonApi.Extensions;

/// <summary>
/// Helpers for resolving an <see cref="ErrorException"/>'s resource key (+ optional args) into the
/// localized message at the envelope boundary, using the request's <c>CurrentUICulture</c>.
/// </summary>
public static class LocalizerExtensions
{
    /// <summary>
    /// Resolves <see cref="ErrorException.MessageKey"/> against the localizer, applying
    /// <see cref="ErrorException.Args"/> when present. A missing key returns the key itself (framework
    /// behaviour), never throwing.
    /// </summary>
    public static string LocalizeError(this IStringLocalizer localizer, ErrorException exception) =>
        exception.Args is { Length: > 0 }
            ? localizer[exception.MessageKey, exception.Args]
            : localizer[exception.MessageKey];
}
