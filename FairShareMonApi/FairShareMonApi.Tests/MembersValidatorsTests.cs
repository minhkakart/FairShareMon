using FairShareMonApi.Models.Members;
using FairShareMonApi.Validators.Members;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the member request validators. Message texts are pinned per the Step-9 list;
/// the camelCase <c>name</c> key into <c>error.fields</c> is covered end-to-end by
/// <c>MembersEndpointTests</c>.
/// </summary>
[UseCulture("vi-VN")]
public class CreateMemberRequestValidatorTests
{
    private readonly CreateMemberRequestValidator _validator = new();

    [Theory]
    [InlineData("An")]
    [InlineData("Nguyễn Văn A")]
    [InlineData("hai bạn tên An")] // free-form, spaces allowed
    public void Validate_ValidName_Passes(string name)
    {
        _validator.TestValidate(new CreateMemberRequest { Name = name }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(new CreateMemberRequest { Name = "" });

        result.ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên thành viên không được để trống.");
    }

    [Fact]
    public void Validate_WhitespaceOnlyName_FailsRequired()
    {
        var result = _validator.TestValidate(new CreateMemberRequest { Name = "   " });

        result.ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên thành viên không được để trống.");
    }

    [Fact]
    public void Validate_Name100Chars_Passes()
    {
        _validator.TestValidate(new CreateMemberRequest { Name = new string('a', 100) })
            .ShouldNotHaveValidationErrorFor(request => request.Name);
    }

    [Fact]
    public void Validate_NameOver100Chars_FailsWithMaxLengthMessage()
    {
        var result = _validator.TestValidate(new CreateMemberRequest { Name = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên thành viên không được vượt quá 100 ký tự.");
    }
}

[UseCulture("vi-VN")]
public class UpdateMemberRequestValidatorTests
{
    private readonly UpdateMemberRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidName_Passes()
    {
        _validator.TestValidate(new UpdateMemberRequest { Name = "Tên mới" }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        var result = _validator.TestValidate(new UpdateMemberRequest { Name = "" });

        result.ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên thành viên không được để trống.");
    }

    [Fact]
    public void Validate_NameOver100Chars_FailsWithMaxLengthMessage()
    {
        var result = _validator.TestValidate(new UpdateMemberRequest { Name = new string('a', 101) });

        result.ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên thành viên không được vượt quá 100 ký tự.");
    }
}
