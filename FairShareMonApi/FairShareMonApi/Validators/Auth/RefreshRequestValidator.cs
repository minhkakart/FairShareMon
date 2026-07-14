using FairShareMonApi.Models.Auth;
using FluentValidation;

namespace FairShareMonApi.Validators.Auth;

public class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(request => request.RefreshToken)
            .NotEmpty().WithMessage("Mã gia hạn phiên không được để trống.");
    }
}
