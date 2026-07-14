using FairShareMonApi.Models.Categories;
using FairShareMonApi.Validators.Categories;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the category request validators (OQ2/OQ6): name required/trim-relevant/max-100,
/// color required + <c>#RRGGBB</c> hex regex, icon optional + max-50. Message texts are pinned per the
/// Step-9 list; the camelCase <c>name</c>/<c>color</c>/<c>icon</c> keys into <c>error.fields</c> are
/// covered end-to-end by <c>CategoriesEndpointTests</c>.
/// </summary>
public class CreateCategoryRequestValidatorTests
{
    private readonly CreateCategoryRequestValidator _validator = new();

    private static CreateCategoryRequest Valid(string? name = "Ăn uống", string color = "#F97316", string? icon = "🍜") =>
        new() { Name = name!, Color = color, Icon = icon };

    [Theory]
    [InlineData("Ăn uống")]
    [InlineData("An uong")]
    [InlineData("Chi phí phát sinh")] // free-form, spaces allowed
    public void Validate_ValidName_Passes(string name)
    {
        _validator.TestValidate(Valid(name: name)).ShouldNotHaveValidationErrorFor(request => request.Name);
    }

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(name: ""))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên danh mục không được để trống.");
    }

    [Fact]
    public void Validate_WhitespaceOnlyName_FailsRequired()
    {
        _validator.TestValidate(Valid(name: "   "))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên danh mục không được để trống.");
    }

    [Fact]
    public void Validate_Name100Chars_Passes()
    {
        _validator.TestValidate(Valid(name: new string('a', 100)))
            .ShouldNotHaveValidationErrorFor(request => request.Name);
    }

    [Fact]
    public void Validate_NameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(name: new string('a', 101)))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên danh mục không được vượt quá 100 ký tự.");
    }

    [Fact]
    public void Validate_EmptyColor_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(color: ""))
            .ShouldHaveValidationErrorFor(request => request.Color)
            .WithErrorMessage("Màu danh mục không được để trống.");
    }

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    [InlineData("#a1b2c3")]
    [InlineData("#F97316")]
    public void Validate_ValidHexColor_Passes(string color)
    {
        _validator.TestValidate(Valid(color: color)).ShouldNotHaveValidationErrorFor(request => request.Color);
    }

    [Theory]
    [InlineData("FFFFFF")]    // missing '#'
    [InlineData("#FFF")]      // too short (3 digits)
    [InlineData("#FFFFFFF")]  // too long (7 digits)
    [InlineData("#GGGGGG")]   // non-hex characters
    [InlineData("#12 456")]   // whitespace inside
    [InlineData("red")]       // named color
    public void Validate_InvalidHexColor_FailsWithInvalidMessage(string color)
    {
        _validator.TestValidate(Valid(color: color))
            .ShouldHaveValidationErrorFor(request => request.Color)
            .WithErrorMessage("Màu danh mục không hợp lệ.");
    }

    [Fact]
    public void Validate_NullIcon_Passes()
    {
        _validator.TestValidate(Valid(icon: null)).ShouldNotHaveValidationErrorFor(request => request.Icon);
    }

    [Fact]
    public void Validate_Icon50Chars_Passes()
    {
        _validator.TestValidate(Valid(icon: new string('x', 50)))
            .ShouldNotHaveValidationErrorFor(request => request.Icon);
    }

    [Fact]
    public void Validate_IconOver50Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(icon: new string('x', 51)))
            .ShouldHaveValidationErrorFor(request => request.Icon)
            .WithErrorMessage("Icon danh mục không được vượt quá 50 ký tự.");
    }
}

public class UpdateCategoryRequestValidatorTests
{
    private readonly UpdateCategoryRequestValidator _validator = new();

    private static UpdateCategoryRequest Valid(string? name = "Ăn uống", string color = "#F97316", string? icon = "🍜") =>
        new() { Name = name!, Color = color, Icon = icon };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(name: ""))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên danh mục không được để trống.");
    }

    [Fact]
    public void Validate_NameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(name: new string('a', 101)))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên danh mục không được vượt quá 100 ký tự.");
    }

    [Fact]
    public void Validate_InvalidHexColor_FailsWithInvalidMessage()
    {
        _validator.TestValidate(Valid(color: "#FFF"))
            .ShouldHaveValidationErrorFor(request => request.Color)
            .WithErrorMessage("Màu danh mục không hợp lệ.");
    }

    [Fact]
    public void Validate_IconOver50Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(icon: new string('x', 51)))
            .ShouldHaveValidationErrorFor(request => request.Icon)
            .WithErrorMessage("Icon danh mục không được vượt quá 50 ký tự.");
    }
}
