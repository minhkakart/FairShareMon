using FairShareMonApi.Models.Auth;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Validators.Auth;
using FairShareMonApi.Validators.Members;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// English (en-US) counterparts to the Vietnamese validator assertions, proving FluentValidation
/// <c>.WithMessage</c> texts localize by culture (Localization subsystem D3/D9). The validators are
/// constructed with <c>new()</c> - they fall back to <see cref="FairShareMonApi.Localization.SharedStringLocalizer"/>,
/// which honours the <see cref="UseCultureAttribute"/>-pinned <c>CurrentUICulture</c> - so the same code
/// path that runs the existing vi-VN tests now yields English. Includes a max-length rule and a
/// <c>{0},{1}</c>-range rule to verify placeholder interpolation carries across cultures. The vi-VN side
/// remains covered by the migrated per-area <c>*ValidatorsTests</c> classes.
/// </summary>
[UseCulture("en-US")]
public class LocalizationValidatorEnglishTests
{
    private readonly CreateMemberRequestValidator _member = new();
    private readonly RegisterRequestValidator _register = new();

    [Fact]
    public void Member_EmptyName_EnglishRequiredMessage()
    {
        _member.TestValidate(new CreateMemberRequest { Name = "" })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Member name must not be empty.");
    }

    [Fact]
    public void Member_NameOver100Chars_EnglishMaxLengthMessage_WithInterpolatedLimit()
    {
        _member.TestValidate(new CreateMemberRequest { Name = new string('a', 101) })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Member name must not exceed 100 characters.");
    }

    [Fact]
    public void Register_EmptyUsername_EnglishRequiredMessage()
    {
        _register.TestValidate(new RegisterRequest { Username = "", Password = "password8" })
            .ShouldHaveValidationErrorFor(request => request.Username)
            .WithErrorMessage("Username must not be empty.");
    }

    [Fact]
    public void Register_UsernameOutOfRange_EnglishLengthMessage_WithInterpolatedBounds()
    {
        _register.TestValidate(new RegisterRequest { Username = "ab", Password = "password8" })
            .ShouldHaveValidationErrorFor(request => request.Username)
            .WithErrorMessage("Username must be between 3 and 32 characters.");
    }

    [Fact]
    public void Register_PasswordTooShort_EnglishMinLengthMessage_WithInterpolatedLimit()
    {
        _register.TestValidate(new RegisterRequest { Username = "valid_user", Password = "1234567" })
            .ShouldHaveValidationErrorFor(request => request.Password)
            .WithErrorMessage("Password must be at least 8 characters.");
    }
}

/// <summary>
/// A focused vi-VN restatement of the same representative rules, co-located with the English tests so the
/// two cultures are visibly proven side by side over the identical (localizer-fallback) code path.
/// </summary>
[UseCulture("vi-VN")]
public class LocalizationValidatorVietnameseTests
{
    private readonly CreateMemberRequestValidator _member = new();
    private readonly RegisterRequestValidator _register = new();

    [Fact]
    public void Member_NameOver100Chars_VietnameseMaxLengthMessage_WithInterpolatedLimit()
    {
        _member.TestValidate(new CreateMemberRequest { Name = new string('a', 101) })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên thành viên không được vượt quá 100 ký tự.");
    }

    [Fact]
    public void Register_UsernameOutOfRange_VietnameseLengthMessage_WithInterpolatedBounds()
    {
        _register.TestValidate(new RegisterRequest { Username = "ab", Password = "password8" })
            .ShouldHaveValidationErrorFor(request => request.Username)
            .WithErrorMessage("Tên đăng nhập phải có từ 3 đến 32 ký tự.");
    }
}
