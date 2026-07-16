using FairShareMonApi.Models.Tags;
using FairShareMonApi.Validators.Tags;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the tag request validators (OQ6/OQ9 — name-only): name required
/// (whitespace-only rejected) and at most 100 chars. Message texts are pinned per the Step-9 list;
/// the camelCase <c>name</c> key into <c>error.fields</c> is covered end-to-end by
/// <c>TagsEndpointTests</c>.
/// </summary>
[UseCulture("vi-VN")]
public class CreateTagRequestValidatorTests
{
    private readonly CreateTagRequestValidator _validator = new();

    [Theory]
    [InlineData("Công tác")]
    [InlineData("cong tac")]
    [InlineData("Du lịch hè 2026")] // free-form, spaces allowed
    public void Validate_ValidName_Passes(string name)
    {
        _validator.TestValidate(new CreateTagRequest { Name = name }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(new CreateTagRequest { Name = "" })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên nhãn không được để trống.");
    }

    [Fact]
    public void Validate_WhitespaceOnlyName_FailsRequired()
    {
        _validator.TestValidate(new CreateTagRequest { Name = "   " })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên nhãn không được để trống.");
    }

    [Fact]
    public void Validate_Name100Chars_Passes()
    {
        _validator.TestValidate(new CreateTagRequest { Name = new string('a', 100) })
            .ShouldNotHaveValidationErrorFor(request => request.Name);
    }

    [Fact]
    public void Validate_NameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(new CreateTagRequest { Name = new string('a', 101) })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên nhãn không được vượt quá 100 ký tự.");
    }
}

[UseCulture("vi-VN")]
public class UpdateTagRequestValidatorTests
{
    private readonly UpdateTagRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidName_Passes()
    {
        _validator.TestValidate(new UpdateTagRequest { Name = "Tên mới" }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(new UpdateTagRequest { Name = "" })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên nhãn không được để trống.");
    }

    [Fact]
    public void Validate_NameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(new UpdateTagRequest { Name = new string('a', 101) })
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên nhãn không được vượt quá 100 ký tự.");
    }
}
