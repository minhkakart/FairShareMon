using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FairShareMonApi.Localization;

/// <summary>
/// A process-wide <see cref="IStringLocalizer{StringResources}"/> built directly on the framework's
/// <see cref="ResourceManagerStringLocalizerFactory"/> (default <see cref="LocalizationOptions"/>, i.e.
/// no <c>ResourcesPath</c>, matching how <c>AddLocalization()</c> is wired). It resolves the exact same
/// resx family as the DI-provided localizer and honours <c>CurrentUICulture</c>.
/// <para>
/// It is used only as the fallback for the optional localizer parameter on FluentValidation validators,
/// so a validator constructed without DI (e.g. <c>new XValidator()</c> in unit tests) still localizes
/// correctly instead of throwing. In the running application every validator is built by the container
/// and receives the injected localizer.
/// </para>
/// </summary>
public static class SharedStringLocalizer
{
    public static IStringLocalizer<StringResources> Instance { get; } =
        new StringLocalizer<StringResources>(
            new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions()),
                NullLoggerFactory.Instance));
}
