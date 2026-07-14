using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Tags;
using FluentValidation;

namespace FairShareMonApi.Validators.Tags;

/// <summary>
/// Tag name rules (OQ6): required (whitespace-only rejected) and at most 100 chars. Uniqueness is
/// enforced in the service (active-only). The service trims the name before persisting.
/// </summary>
public class CreateTagRequestValidator : AbstractValidator<CreateTagRequest>
{
    public CreateTagRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Tên nhãn không được để trống.")
            .MaximumLength(Tag.NameMaxLength)
            .WithMessage($"Tên nhãn không được vượt quá {Tag.NameMaxLength} ký tự.");
    }
}
