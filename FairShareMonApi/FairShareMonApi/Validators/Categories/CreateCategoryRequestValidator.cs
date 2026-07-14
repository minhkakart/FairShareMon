using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Categories;
using FluentValidation;

namespace FairShareMonApi.Validators.Categories;

/// <summary>
/// Category create rules (OQ2/OQ6): <c>Name</c> required (whitespace-only rejected) and at most 100
/// chars; <c>Color</c> required and a valid <c>#RRGGBB</c> hex string; <c>Icon</c> optional, at most
/// 50 chars. Uniqueness is enforced in the service (active-only, no partial index). The service
/// trims the name before persisting.
/// </summary>
public class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    /// <summary>Hex color pattern <c>#RRGGBB</c> (OQ2).</summary>
    public const string ColorPattern = "^#[0-9A-Fa-f]{6}$";

    public CreateCategoryRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Tên danh mục không được để trống.")
            .MaximumLength(Category.NameMaxLength)
            .WithMessage($"Tên danh mục không được vượt quá {Category.NameMaxLength} ký tự.");

        RuleFor(request => request.Color)
            .NotEmpty().WithMessage("Màu danh mục không được để trống.")
            .Matches(ColorPattern).WithMessage("Màu danh mục không hợp lệ.");

        RuleFor(request => request.Icon)
            .MaximumLength(Category.IconMaxLength)
            .WithMessage($"Icon danh mục không được vượt quá {Category.IconMaxLength} ký tự.");
    }
}
