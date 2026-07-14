using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Admin;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>
/// Grant rules (M11 OQ15): <c>Amount &gt;= 0</c> (0 = cấp miễn phí); <c>Currency</c> tùy chọn, tối đa 3
/// ký tự; <c>Reference</c>/<c>Note</c> giới hạn độ dài. Sai -&gt; lỗi kiểm tra dữ liệu 1001.
/// </summary>
public class GrantTierRequestValidator : AbstractValidator<GrantTierRequest>
{
    public GrantTierRequestValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Số tiền không được âm.");

        RuleFor(request => request.Currency)
            .MaximumLength(TierGrant.CurrencyMaxLength)
            .WithMessage($"Đơn vị tiền tệ không được vượt quá {TierGrant.CurrencyMaxLength} ký tự.")
            .When(request => !string.IsNullOrEmpty(request.Currency));

        RuleFor(request => request.Reference)
            .MaximumLength(TierGrant.ReferenceMaxLength)
            .WithMessage($"Mã tham chiếu không được vượt quá {TierGrant.ReferenceMaxLength} ký tự.");

        RuleFor(request => request.Note)
            .MaximumLength(TierGrant.NoteMaxLength)
            .WithMessage($"Ghi chú không được vượt quá {TierGrant.NoteMaxLength} ký tự.");
    }
}
