using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>
/// Grant rules (M11 OQ15): <c>Amount &gt;= 0</c> (0 = cấp miễn phí); <c>Currency</c> tùy chọn, tối đa 3
/// ký tự; <c>Reference</c>/<c>Note</c> giới hạn độ dài. Sai -&gt; lỗi kiểm tra dữ liệu 1001.
/// </summary>
public class GrantTierRequestValidator : AbstractValidator<GrantTierRequest>
{
    public GrantTierRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Amount)
            .GreaterThanOrEqualTo(0).WithMessage(_ => localizer[MessageKeys.Validation.Common.AmountNegative].Value);

        RuleFor(request => request.Currency)
            .MaximumLength(TierGrant.CurrencyMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Admin.CurrencyTooLong, TierGrant.CurrencyMaxLength].Value)
            .When(request => !string.IsNullOrEmpty(request.Currency));

        RuleFor(request => request.Reference)
            .MaximumLength(TierGrant.ReferenceMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Admin.ReferenceTooLong, TierGrant.ReferenceMaxLength].Value);

        RuleFor(request => request.Note)
            .MaximumLength(TierGrant.NoteMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Common.NoteTooLong, TierGrant.NoteMaxLength].Value);
    }
}
