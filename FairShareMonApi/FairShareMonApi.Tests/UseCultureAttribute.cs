using System.Globalization;
using System.Reflection;
using Xunit.Sdk;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pins <see cref="CultureInfo.CurrentCulture"/> + <see cref="CultureInfo.CurrentUICulture"/> for the
/// duration of a test and restores them afterwards, so locale-sensitive assertions (localized
/// validator/error messages resolved via <c>IStringLocalizer</c>) are deterministic regardless of the
/// host machine's ambient culture. Applied at class level to every message-asserting validator test
/// (Localization subsystem D9), and at method/class level on the new en-US culture tests.
/// <para>
/// Implemented as an xUnit <see cref="BeforeAfterTestAttribute"/>: <see cref="Before"/> runs on the
/// test's own thread immediately before the test body (so the culture is set before the validator's
/// <c>.WithMessage(_ =&gt; localizer[...])</c> lambda resolves), and <see cref="After"/> restores it.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class UseCultureAttribute(string culture) : BeforeAfterTestAttribute
{
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo(culture);
    private CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public override void Before(MethodInfo methodUnderTest)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
    }

    public override void After(MethodInfo methodUnderTest)
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}

/// <summary>
/// Imperative counterpart to <see cref="UseCultureAttribute"/> for tests that need to switch culture
/// several times within a single method (e.g. asserting the same key in vi-VN then en-US). Restores the
/// previous culture on <see cref="Dispose"/>.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly CultureInfo _originalUiCulture;

    public CultureScope(string culture)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;
        var target = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = target;
        CultureInfo.CurrentUICulture = target;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}
