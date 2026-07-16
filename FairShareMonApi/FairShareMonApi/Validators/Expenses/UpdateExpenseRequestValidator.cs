using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Expenses;

/// <summary>
/// Expense update-general-info rules (OQ16): same field policy as create minus shares - <c>Name</c>
/// required + max 200; <c>Description</c> optional + max 1000; <c>ExpenseTime</c> required. Tag-set
/// full-replace and link integrity are handled in the repository transaction.
/// </summary>
public class UpdateExpenseRequestValidator : AbstractValidator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Expense.NameRequired].Value)
            .MaximumLength(Expense.NameMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Expense.NameTooLong, Expense.NameMaxLength].Value);

        RuleFor(request => request.Description)
            .MaximumLength(Expense.DescriptionMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Expense.DescriptionTooLong, Expense.DescriptionMaxLength].Value);

        RuleFor(request => request.ExpenseTime)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Expense.ExpenseTimeRequired].Value);
    }
}
