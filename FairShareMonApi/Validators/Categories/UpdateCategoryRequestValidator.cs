using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Categories;
using FluentValidation;

namespace FairShareMonApi.Validators.Categories;

/// <summary>
/// Category update follows the same field policy as create (OQ2/OQ6): <c>Name</c> required + max 100;
/// <c>Color</c> required + valid <c>#RRGGBB</c> hex; <c>Icon</c> optional + max 50. This endpoint
/// cannot change the default flag (OQ7). The service trims the name before persisting.
/// </summary>
public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Tên danh mục không được để trống.")
            .MaximumLength(Category.NameMaxLength)
            .WithMessage($"Tên danh mục không được vượt quá {Category.NameMaxLength} ký tự.");

        RuleFor(request => request.Color)
            .NotEmpty().WithMessage("Màu danh mục không được để trống.")
            .Matches(CreateCategoryRequestValidator.ColorPattern).WithMessage("Màu danh mục không hợp lệ.");

        RuleFor(request => request.Icon)
            .MaximumLength(Category.IconMaxLength)
            .WithMessage($"Icon danh mục không được vượt quá {Category.IconMaxLength} ký tự.");
    }
}
