using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;
using FluentValidation;

namespace FairShareMonApi.Validators.Expenses;

/// <summary>
/// Expense update-general-info rules (OQ16): same field policy as create minus shares - <c>Name</c>
/// required + max 200; <c>Description</c> optional + max 1000; <c>ExpenseTime</c> required. Tag-set
/// full-replace and link integrity are handled in the repository transaction.
/// </summary>
public class UpdateExpenseRequestValidator : AbstractValidator<UpdateExpenseRequest>
{
    public UpdateExpenseRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Tên phiếu chi tiêu không được để trống.")
            .MaximumLength(Expense.NameMaxLength)
            .WithMessage($"Tên phiếu chi tiêu không được vượt quá {Expense.NameMaxLength} ký tự.");

        RuleFor(request => request.Description)
            .MaximumLength(Expense.DescriptionMaxLength)
            .WithMessage($"Mô tả không được vượt quá {Expense.DescriptionMaxLength} ký tự.");

        RuleFor(request => request.ExpenseTime)
            .NotEmpty().WithMessage("Thời điểm chi không được để trống.");
    }
}
