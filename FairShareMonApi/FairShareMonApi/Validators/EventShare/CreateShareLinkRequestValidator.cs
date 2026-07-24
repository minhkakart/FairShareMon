using FairShareMonApi.Models.Share;
using FluentValidation;

namespace FairShareMonApi.Validators.EventShare;

/// <summary>
/// Create-share-link rules: <c>BankAccountUuid</c> is optional (null OK) but, when present, must be
/// non-empty and within the UUID column length. The real bank checks happen in the service (a missing
/// override -&gt; <c>BankAccountNotFound</c> 12000). Namespaced <c>EventShare</c> (not <c>Share</c>) so
/// the segment does not shadow the <c>Share</c> entity referenced by sibling validators.
/// </summary>
public class CreateShareLinkRequestValidator : AbstractValidator<CreateShareLinkRequest>
{
    /// <summary>Max length of an external UUID reference (matches the uuid column, 64).</summary>
    public const int BankAccountUuidMaxLength = 64;

    public CreateShareLinkRequestValidator()
    {
        When(request => request.BankAccountUuid is not null, () =>
            RuleFor(request => request.BankAccountUuid)
                .NotEmpty()
                .MaximumLength(BankAccountUuidMaxLength));
    }
}
