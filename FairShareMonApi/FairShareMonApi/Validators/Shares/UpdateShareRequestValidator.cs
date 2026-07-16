using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Shares;

/// <summary>
/// Share update rules: <c>MemberUuid</c> required; <c>Amount</c> non-negative (§4.3); <c>Note</c> at
/// most 500 (OQ16). Change-member link integrity + owner-rep guard are enforced in the repository
/// transaction.
/// </summary>
public class UpdateShareRequestValidator : AbstractValidator<UpdateShareRequest>
{
    public UpdateShareRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.MemberUuid)
            .NotEmpty().WithMessage(_ => localizer[MessageKeys.Validation.Share.MemberRequired].Value);

        RuleFor(request => request.Amount)
            .GreaterThanOrEqualTo(0).WithMessage(_ => localizer[MessageKeys.Validation.Common.AmountNegative].Value);

        RuleFor(request => request.Note)
            .MaximumLength(Share.NoteMaxLength)
            .WithMessage(_ => localizer[MessageKeys.Validation.Common.NoteTooLong, Share.NoteMaxLength].Value);
    }
}
