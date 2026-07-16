using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>Revoke rule (M11 OQ4): chỉ giới hạn độ dài ghi chú tùy chọn.</summary>
public class RevokeTierRequestValidator : AbstractValidator<RevokeTierRequest>
{
    public RevokeTierRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Note)
            .MaximumLength(TierGrant.NoteMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Common.NoteTooLong, TierGrant.NoteMaxLength].Value);
    }
}
