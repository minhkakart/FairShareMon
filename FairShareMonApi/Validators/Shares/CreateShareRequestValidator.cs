using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Shares;
using FluentValidation;

namespace FairShareMonApi.Validators.Shares;

/// <summary>
/// Share add rules: <c>MemberUuid</c> required; <c>Amount</c> non-negative (§4.3); <c>Note</c> at most
/// 500 (OQ16). Member link integrity + duplicate-member are enforced in the repository transaction.
/// </summary>
public class CreateShareRequestValidator : AbstractValidator<CreateShareRequest>
{
    public CreateShareRequestValidator()
    {
        RuleFor(request => request.MemberUuid)
            .NotEmpty().WithMessage("Thành viên của phần gánh không được để trống.");

        RuleFor(request => request.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Số tiền không được âm.");

        RuleFor(request => request.Note)
            .MaximumLength(Share.NoteMaxLength)
            .WithMessage($"Ghi chú không được vượt quá {Share.NoteMaxLength} ký tự.");
    }
}
