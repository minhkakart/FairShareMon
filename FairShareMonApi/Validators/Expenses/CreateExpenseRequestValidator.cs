using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;
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
    public CreateExpenseRequestValidator()
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

        When(request => request.Shares is not null, () =>
        {
            RuleForEach(request => request.Shares).ChildRules(share =>
            {
                share.RuleFor(input => input.MemberUuid)
                    .NotEmpty().WithMessage("Thành viên của phần gánh không được để trống.");

                share.RuleFor(input => input.Amount)
                    .GreaterThanOrEqualTo(0).WithMessage("Số tiền không được âm.");

                share.RuleFor(input => input.Note)
                    .MaximumLength(Share.NoteMaxLength)
                    .WithMessage($"Ghi chú không được vượt quá {Share.NoteMaxLength} ký tự.");
            });
        });
    }
}
