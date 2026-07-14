using FairShareMonApi.Models.Auth;
using FairShareMonApi.Validators.Auth;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the four auth request validators. Message texts are pinned here because the
/// planning doc's test list explicitly requires the Vietnamese texts; property names are asserted
/// as declared (PascalCase) - the camelCase mapping into <c>error.fields</c> is covered end-to-end
/// by <c>AuthEndpointTests</c>.
/// </summary>
public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Request(string username = "valid_user-1.a", string password = "password8") =>
        new() { Username = username, Password = password };

    [Theory]
    [InlineData("abc")] // min length 3
    [InlineData("UPPERCASE_OK")] // uppercase accepted, stored lowercase later
    [InlineData("user_name.with-all_chars.09")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")] // max length 32
    public void Validate_ValidUsername_Passes(string username)
    {
        var result = _validator.TestValidate(Request(username: username));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyUsername_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(Request(username: ""));

        result.ShouldHaveValidationErrorFor(request => request.Username)
            .WithErrorMessage("Tên đăng nhập không được để trống.");
    }

    [Theory]
    [InlineData("ab")] // 2 chars
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33 chars
    public void Validate_UsernameLengthOutOfRange_FailsWithLengthMessage(string username)
    {
        var result = _validator.TestValidate(Request(username: username));

        result.ShouldHaveValidationErrorFor(request => request.Username)
            .WithErrorMessage("Tên đăng nhập phải có từ 3 đến 32 ký tự.");
    }

    [Theory]
    [InlineData("việtnam")] // diacritics
    [InlineData("user name")] // space
    [InlineData("user@name")] // symbol outside _ . -
    public void Validate_UsernameWithForbiddenCharacters_FailsWithPatternMessage(string username)
    {
        var result = _validator.TestValidate(Request(username: username));

        result.ShouldHaveValidationErrorFor(request => request.Username)
            .WithErrorMessage("Tên đăng nhập chỉ được chứa chữ cái không dấu, chữ số và các ký tự _ . -");
    }

    [Fact]
    public void Validate_EmptyPassword_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(Request(password: ""));

        result.ShouldHaveValidationErrorFor(request => request.Password)
            .WithErrorMessage("Mật khẩu không được để trống.");
    }

    [Fact]
    public void Validate_PasswordShorterThan8Chars_FailsWithMinLengthMessage()
    {
        var result = _validator.TestValidate(Request(password: "1234567"));

        result.ShouldHaveValidationErrorFor(request => request.Password)
            .WithErrorMessage("Mật khẩu phải có ít nhất 8 ký tự.");
    }

    [Fact]
    public void Validate_Password72Bytes_Passes()
    {
        var result = _validator.TestValidate(Request(password: new string('a', 72)));

        result.ShouldNotHaveValidationErrorFor(request => request.Password);
    }

    [Fact]
    public void Validate_Password73Bytes_FailsWithByteLimitMessage()
    {
        var result = _validator.TestValidate(Request(password: new string('a', 73)));

        result.ShouldHaveValidationErrorFor(request => request.Password)
            .WithErrorMessage("Mật khẩu không được vượt quá 72 byte.");
    }

    [Fact]
    public void Validate_MultibytePassword_IsMeasuredInBytesNotChars()
    {
        // 'ộ' is 3 UTF-8 bytes: 25 chars = 75 bytes - over the limit although only 25 characters.
        var result = _validator.TestValidate(Request(password: new string('ộ', 25)));

        result.ShouldHaveValidationErrorFor(request => request.Password)
            .WithErrorMessage("Mật khẩu không được vượt quá 72 byte.");

        // 24 chars = 72 bytes - exactly at the limit.
        _validator.TestValidate(Request(password: new string('ộ', 24)))
            .ShouldNotHaveValidationErrorFor(request => request.Password);
    }
}

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void Validate_BothFieldsPresent_Passes()
    {
        // Login has NO policy rules (length/pattern) - credential checking is the service's job.
        var result = _validator.TestValidate(new LoginRequest { Username = "AnyThing!Even Invalid", Password = "x" });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyUsername_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(new LoginRequest { Username = "", Password = "secret" });

        result.ShouldHaveValidationErrorFor(request => request.Username)
            .WithErrorMessage("Tên đăng nhập không được để trống.");
    }

    [Fact]
    public void Validate_EmptyPassword_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(new LoginRequest { Username = "alice", Password = "" });

        result.ShouldHaveValidationErrorFor(request => request.Password)
            .WithErrorMessage("Mật khẩu không được để trống.");
    }
}

public class RefreshRequestValidatorTests
{
    private readonly RefreshRequestValidator _validator = new();

    [Fact]
    public void Validate_TokenPresent_Passes()
    {
        var result = _validator.TestValidate(new RefreshRequest { RefreshToken = "any-opaque-string" });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyToken_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(new RefreshRequest { RefreshToken = "" });

        result.ShouldHaveValidationErrorFor(request => request.RefreshToken)
            .WithErrorMessage("Mã gia hạn phiên không được để trống.");
    }
}

public class ChangePasswordRequestValidatorTests
{
    private readonly ChangePasswordRequestValidator _validator = new();

    private static ChangePasswordRequest Request(string current = "current-pw", string next = "password8") =>
        new() { CurrentPassword = current, NewPassword = next };

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        _validator.TestValidate(Request()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NewPasswordEqualsCurrent_Passes()
    {
        // OQ3 decision: reusing the same password is deliberately allowed - no equality rule.
        var result = _validator.TestValidate(Request(current: "same-password", next: "same-password"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyCurrentPassword_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(Request(current: ""));

        result.ShouldHaveValidationErrorFor(request => request.CurrentPassword)
            .WithErrorMessage("Mật khẩu hiện tại không được để trống.");
    }

    [Fact]
    public void Validate_EmptyNewPassword_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(Request(next: ""));

        result.ShouldHaveValidationErrorFor(request => request.NewPassword)
            .WithErrorMessage("Mật khẩu mới không được để trống.");
    }

    [Fact]
    public void Validate_NewPasswordShorterThan8Chars_FailsWithMinLengthMessage()
    {
        var result = _validator.TestValidate(Request(next: "1234567"));

        result.ShouldHaveValidationErrorFor(request => request.NewPassword)
            .WithErrorMessage("Mật khẩu mới phải có ít nhất 8 ký tự.");
    }

    [Fact]
    public void Validate_NewPasswordOver72Bytes_FailsWithByteLimitMessage()
    {
        var result = _validator.TestValidate(Request(next: new string('a', 73)));

        result.ShouldHaveValidationErrorFor(request => request.NewPassword)
            .WithErrorMessage("Mật khẩu mới không được vượt quá 72 byte.");
    }
}
