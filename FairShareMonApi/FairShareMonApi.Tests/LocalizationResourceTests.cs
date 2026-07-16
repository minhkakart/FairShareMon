using System.Globalization;
using System.Reflection;
using System.Resources;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Extensions;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no I/O) for the Localization subsystem's resource layer: the
/// <see cref="IStringLocalizer{StringResources}"/> resolution path (via the same
/// <see cref="SharedStringLocalizer"/> factory the app wires through <c>AddLocalization()</c>), the
/// neutral (vi-VN) / en-US satellite / unknown-culture fallback behaviour, <c>{0}</c> interpolation, and
/// a resx-integrity guard that every <see cref="MessageKeys"/> constant is actually present (non-empty) in
/// BOTH resx files. Cultures are pinned explicitly per assertion so results are deterministic regardless
/// of the host machine's ambient culture.
/// </summary>
public class LocalizationResourceTests
{
    private static readonly IStringLocalizer Localizer = SharedStringLocalizer.Instance;

    // ---- Representative keys resolve per culture --------------------------------------------------

    [Fact]
    public void Localizer_ResolvesRepresentativeKeys_InVietnamese()
    {
        using var _ = new CultureScope("vi-VN");

        Assert.Equal("Không tìm thấy thành viên.", Localizer[MessageKeys.Error.MemberNotFound]);
        Assert.Equal("Hệ thống hoạt động bình thường.", Localizer[MessageKeys.Success.HealthOk]);
        Assert.Equal("Tên thành viên không được để trống.", Localizer[MessageKeys.Validation.Member.NameRequired]);
        Assert.Equal("Dữ liệu gửi lên không hợp lệ.", Localizer[MessageKeys.Envelope.ValidationFailed]);
    }

    [Fact]
    public void Localizer_ResolvesRepresentativeKeys_InEnglish()
    {
        using var _ = new CultureScope("en-US");

        Assert.Equal("Member not found.", Localizer[MessageKeys.Error.MemberNotFound]);
        Assert.Equal("The system is operating normally.", Localizer[MessageKeys.Success.HealthOk]);
        Assert.Equal("Member name must not be empty.", Localizer[MessageKeys.Validation.Member.NameRequired]);
        Assert.Equal("The submitted data is invalid.", Localizer[MessageKeys.Envelope.ValidationFailed]);
    }

    [Fact]
    public void Localizer_EnglishSatellite_ActuallyLoads_NotNeutralFallback()
    {
        // Guards that the en-US satellite is really loaded (a distinct value from the neutral vi-VN),
        // not silently falling back to the Vietnamese neutral resource.
        string vietnamese, english;
        using (new CultureScope("vi-VN")) vietnamese = Localizer[MessageKeys.Error.MemberNotFound];
        using (new CultureScope("en-US")) english = Localizer[MessageKeys.Error.MemberNotFound];

        Assert.NotEqual(vietnamese, english);
    }

    // ---- Unknown / unsupported culture folds to the neutral (Vietnamese) resource -----------------

    [Fact]
    public void Localizer_UnknownCulture_FallsBackToVietnameseNeutral()
    {
        using var _ = new CultureScope("fr-FR");

        Assert.Equal("Không tìm thấy thành viên.", Localizer[MessageKeys.Error.MemberNotFound]);
        Assert.Equal("Hệ thống hoạt động bình thường.", Localizer[MessageKeys.Success.HealthOk]);
    }

    // ---- {0} interpolation in both cultures ------------------------------------------------------

    [Fact]
    public void Localizer_InterpolatedKey_FormatsArgument_InVietnamese()
    {
        using var _ = new CultureScope("vi-VN");

        var message = Localizer[MessageKeys.Validation.Member.NameTooLong, 100];

        Assert.Equal("Tên thành viên không được vượt quá 100 ký tự.", message);
    }

    [Fact]
    public void Localizer_InterpolatedKey_FormatsArgument_InEnglish()
    {
        using var _ = new CultureScope("en-US");

        var message = Localizer[MessageKeys.Validation.Member.NameTooLong, 100];

        Assert.Equal("Member name must not exceed 100 characters.", message);
    }

    // ---- ErrorException -> localized text via the envelope helper (LocalizeError) -----------------

    [Fact]
    public void LocalizeError_KeyAndArgs_ResolveToLocalizedText_InBothCultures()
    {
        var exception = new ErrorException(
            ErrorCodes.MemberLimitReached, MessageKeys.Error.MemberLimitReached, args: [25]);

        using (new CultureScope("vi-VN"))
        {
            var vi = Localizer.LocalizeError(exception);
            Assert.Equal("Tài khoản Free chỉ được tạo tối đa 25 thành viên. Nâng cấp Premium để bỏ giới hạn.", vi);
        }

        using (new CultureScope("en-US"))
        {
            var en = Localizer.LocalizeError(exception);
            Assert.Equal("Free accounts can create at most 25 members. Upgrade to Premium to remove the limit.", en);
        }
    }

    [Fact]
    public void LocalizeError_NoArgs_ResolvesPlainKey()
    {
        var exception = new ErrorException(ErrorCodes.MemberNotFound, MessageKeys.Error.MemberNotFound);

        using var _ = new CultureScope("en-US");
        Assert.Equal("Member not found.", Localizer.LocalizeError(exception));
    }

    // ---- Smoke: reading a key per culture never throws MissingManifestResourceException -----------

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Localizer_ReadingKey_NeverThrowsMissingManifest(string culture)
    {
        using var _ = new CultureScope(culture);

        var exception = Record.Exception(() =>
        {
            var value = Localizer[MessageKeys.Error.Unauthorized].Value;
            Assert.False(string.IsNullOrWhiteSpace(value));
        });

        Assert.Null(exception);
    }

    // ---- resx integrity: every MessageKeys constant exists (non-empty) in BOTH resx --------------

    [Fact]
    public void EveryMessageKey_ExistsInBothNeutralAndEnglishResx()
    {
        var resourceManager = new ResourceManager(
            typeof(StringResources).FullName!, typeof(StringResources).Assembly);

        // tryParents:false so each set is exactly the keys that culture defines - this is what catches a
        // key that is present in the neutral resx but MISSING from en-US (which would otherwise silently
        // fall back to Vietnamese and go unnoticed), and vice versa.
        var neutralSet = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        var englishSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en-US"), createIfNotExists: true, tryParents: false);

        Assert.NotNull(neutralSet);  // neutral (vi-VN) main-assembly resource must load
        Assert.NotNull(englishSet);  // en-US satellite must load

        var keys = CollectMessageKeyConstants();
        Assert.NotEmpty(keys);

        var missingFromNeutral = new List<string>();
        var missingFromEnglish = new List<string>();
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(neutralSet!.GetString(key))) missingFromNeutral.Add(key);
            if (string.IsNullOrEmpty(englishSet!.GetString(key))) missingFromEnglish.Add(key);
        }

        Assert.True(missingFromNeutral.Count == 0,
            "MessageKeys constants missing/empty in the NEUTRAL (vi-VN) resx: " + string.Join(", ", missingFromNeutral));
        Assert.True(missingFromEnglish.Count == 0,
            "MessageKeys constants missing/empty in the en-US resx: " + string.Join(", ", missingFromEnglish));
    }

    [Fact]
    public void MessageKeys_CoversAllOneHundredTwentyThreeKeys()
    {
        // Sanity anchor on the documented key count (123, per the planning doc's final outcome) so an
        // accidental key deletion/addition surfaces here rather than as a silent gap.
        Assert.Equal(123, CollectMessageKeyConstants().Count);
    }

    /// <summary>Recursively collects every <c>public const string</c> value declared under <see cref="MessageKeys"/>.</summary>
    private static List<string> CollectMessageKeyConstants()
    {
        var values = new List<string>();
        Collect(typeof(MessageKeys), values);
        return values;

        static void Collect(Type type, List<string> sink)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsLiteral && field.FieldType == typeof(string) && field.GetRawConstantValue() is string value)
                    sink.Add(value);

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
                Collect(nested, sink);
        }
    }
}
