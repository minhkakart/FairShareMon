using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

namespace FairShareMonApi.Extensions;

/// <summary>
/// Wires runtime message localization (planning/localization-subsystem.md). Vietnamese is the neutral
/// language (<c>StringResources.resx</c> + <c>[assembly: NeutralResourcesLanguage("vi-VN")]</c>) and
/// en-US is the only satellite; the culture is resolved per request from <c>?culture=</c> then
/// <c>Accept-Language</c> and drives <c>CurrentUICulture</c>, which <c>IStringLocalizer</c> reads.
/// <para>
/// The default culture and supported-cultures list are config-driven (mirroring the timezone feature's
/// <c>App:DefaultTimeZone</c>): <c>App:DefaultCulture</c> (default <c>vi-VN</c>) and
/// <c>App:SupportedCultures</c> (default <c>["vi-VN","en-US"]</c>). Any unsupported/unknown culture folds
/// back to the default (Vietnamese).
/// </para>
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>Config key for the default request culture.</summary>
    public const string DefaultCultureConfigKey = "App:DefaultCulture";

    /// <summary>Config key for the supported-cultures list.</summary>
    public const string SupportedCulturesConfigKey = "App:SupportedCultures";

    /// <summary>Fallback default culture when config is missing.</summary>
    public const string FallbackDefaultCulture = "vi-VN";

    /// <summary>Fallback supported cultures when config is missing.</summary>
    public static readonly string[] FallbackSupportedCultures = ["vi-VN", "en-US"];

    /// <summary>
    /// Registers <c>IStringLocalizer</c> support. No <c>ResourcesPath</c> is set: the resx base name
    /// equals the marker type's full name (<c>FairShareMonApi.Localization.Resources.StringResources</c>),
    /// which already matches the resx manifest name, so an empty resources path resolves correctly.
    /// </summary>
    public static IServiceCollection AddAppLocalization(this IServiceCollection services)
    {
        services.AddLocalization();
        return services;
    }

    /// <summary>
    /// Adds <c>UseRequestLocalization</c> with query-string then Accept-Language providers, the
    /// configured default request culture, and the configured supported cultures. Place next to
    /// <c>RequestTimeZoneMiddleware</c> (right after <c>UseRouting</c>) so <c>CurrentUICulture</c> is set
    /// before any endpoint, filter, validator, or JSON converter runs.
    /// </summary>
    public static IApplicationBuilder UseAppLocalization(this IApplicationBuilder app, IConfiguration configuration)
    {
        var defaultCulture = configuration[DefaultCultureConfigKey];
        if (string.IsNullOrWhiteSpace(defaultCulture))
            defaultCulture = FallbackDefaultCulture;

        var supported = configuration.GetSection(SupportedCulturesConfigKey).Get<string[]>();
        if (supported is null || supported.Length == 0)
            supported = FallbackSupportedCultures;

        var cultures = supported.Select(culture => new CultureInfo(culture)).ToList();

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(defaultCulture),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
            ApplyCurrentCultureToResponseHeaders = true,
            RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider()
            ]
        };

        app.UseRequestLocalization(options);
        return app;
    }
}
