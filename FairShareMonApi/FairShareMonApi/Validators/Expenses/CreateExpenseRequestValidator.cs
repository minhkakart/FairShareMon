using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Expenses;

/// <summary>
/// Expense create rules (OQ16): <c>Name</c> required (whitespace-only rejected) + max 200;
/// <c>Description</c> optional + max 1000; <c>ExpenseTime</c> required; each submitted share's
/// <c>Amount</c> non-negative (§4.3) and <c>Note</c> at most 500. Link integrity (payer/category/tag/
/// member owned + active) and the owner-rep/duplicate rules are enforced in the repository transaction.
/// </summary>
public class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator(IStringLocalizer<StringResources>? localizer = null)
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

        When(request => request.Shares is not null, () =>
        {
            RuleForEach(request => request.Shares).ChildRules(share =>
            {
                share.RuleFor(input => input.MemberUuid)
                    .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Share.MemberRequired].Value);

                share.RuleFor(input => input.Amount)
                    .GreaterThanOrEqualTo(0).WithMessage(_ => localizer[MessageKeys.Validation.Common.AmountNegative].Value);

                share.RuleFor(input => input.Note)
                    .MaximumLength(Share.NoteMaxLength)
                    .WithMessage(_ => localizer[MessageKeys.Validation.Common.NoteTooLong, Share.NoteMaxLength].Value);
            });
        });
    }
}
