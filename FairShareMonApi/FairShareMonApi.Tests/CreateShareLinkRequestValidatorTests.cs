using FairShareMonApi.Models.Share;
using FairShareMonApi.Validators.EventShare;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="CreateShareLinkRequestValidator"/> (no DB). The only client-side rule
/// is on the optional <c>BankAccountUuid</c>: a null value is valid (defer to the default account), an
/// empty string is rejected, and a value over the UUID column length (64) is rejected. The
/// <c>Regenerate</c> flag is unconstrained. The real bank existence check (a missing override -&gt;
/// <c>BankAccountNotFound</c> 12000) lives in the service and is covered by the service/endpoint tests.
/// </summary>
public class CreateShareLinkRequestValidatorTests
{
    private readonly CreateShareLinkRequestValidator _validator = new();

    [Fact]
    public void Validate_BankAccountUuidNull_Passes()
    {
        _validator.TestValidate(new CreateShareLinkRequest { BankAccountUuid = null })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BankAccountUuidNull_WithRegenerate_Passes()
    {
        _validator.TestValidate(new CreateShareLinkRequest { BankAccountUuid = null, Regenerate = true })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BankAccountUuidPresentAndValid_Passes()
    {
        _validator.TestValidate(new CreateShareLinkRequest { BankAccountUuid = "0198a5c2-0000-7000-8000-0000000000ab" })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BankAccountUuidEmpty_Fails()
    {
        _validator.TestValidate(new CreateShareLinkRequest { BankAccountUuid = string.Empty })
            .ShouldHaveValidationErrorFor(request => request.BankAccountUuid);
    }

    [Fact]
    public void Validate_BankAccountUuidOverMaxLength_Fails()
    {
        var tooLong = new string('a', CreateShareLinkRequestValidator.BankAccountUuidMaxLength + 1);

        _validator.TestValidate(new CreateShareLinkRequest { BankAccountUuid = tooLong })
            .ShouldHaveValidationErrorFor(request => request.BankAccountUuid);
    }

    [Fact]
    public void Validate_BankAccountUuidAtMaxLength_Passes()
    {
        var atMax = new string('a', CreateShareLinkRequestValidator.BankAccountUuidMaxLength);

        _validator.TestValidate(new CreateShareLinkRequest { BankAccountUuid = atMax })
            .ShouldNotHaveAnyValidationErrors();
    }
}
