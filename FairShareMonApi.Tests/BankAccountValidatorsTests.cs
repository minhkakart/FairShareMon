using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Validators.Wallet;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the bank-account request validators (M9 OQ5): <c>BankBin</c> exactly 6 digits
/// (<c>^\d{6}$</c>); <c>AccountNumber</c> digits only 6-19 (<c>^\d{6,19}$</c>); <c>BankName</c> and
/// <c>AccountHolderName</c> required + max 100. Pins the Vietnamese message texts; the camelCase
/// <c>error.fields</c> keys are covered end-to-end by <c>BankAccountsEndpointTests</c>.
/// </summary>
public class CreateBankAccountRequestValidatorTests
{
    private readonly CreateBankAccountRequestValidator _validator = new();

    private static CreateBankAccountRequest Valid(
        string bankBin = "970436",
        string bankName = "Vietcombank",
        string accountNumber = "0123456789",
        string accountHolderName = "Nguyen Van A") =>
        new() { BankBin = bankBin, BankName = bankName, AccountNumber = accountNumber, AccountHolderName = accountHolderName };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("970436")]
    [InlineData("000000")]
    [InlineData("999999")]
    public void Validate_ValidBankBin_Passes(string bankBin)
    {
        _validator.TestValidate(Valid(bankBin: bankBin)).ShouldNotHaveValidationErrorFor(request => request.BankBin);
    }

    [Theory]
    [InlineData("")]           // empty -> required message (asserted separately)
    [InlineData("12345")]      // 5 digits
    [InlineData("1234567")]    // 7 digits
    [InlineData("12345a")]     // non-digit
    [InlineData("97043 ")]     // whitespace
    public void Validate_InvalidBankBin_Fails(string bankBin)
    {
        _validator.TestValidate(Valid(bankBin: bankBin)).ShouldHaveValidationErrorFor(request => request.BankBin);
    }

    [Fact]
    public void Validate_EmptyBankBin_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(bankBin: ""))
            .ShouldHaveValidationErrorFor(request => request.BankBin)
            .WithErrorMessage("Mã ngân hàng (BIN) không được để trống.");
    }

    [Fact]
    public void Validate_NonSixDigitBankBin_FailsWithFormatMessage()
    {
        _validator.TestValidate(Valid(bankBin: "12345"))
            .ShouldHaveValidationErrorFor(request => request.BankBin)
            .WithErrorMessage("Mã ngân hàng (BIN) phải gồm đúng 6 chữ số.");
    }

    [Theory]
    [InlineData("123456")]                  // 6 digits (min)
    [InlineData("1234567890123456789")]     // 19 digits (max)
    public void Validate_ValidAccountNumber_Passes(string accountNumber)
    {
        _validator.TestValidate(Valid(accountNumber: accountNumber)).ShouldNotHaveValidationErrorFor(request => request.AccountNumber);
    }

    [Theory]
    [InlineData("12345")]                      // 5 digits (too short)
    [InlineData("12345678901234567890")]       // 20 digits (too long)
    [InlineData("12345678a")]                  // non-digit
    [InlineData("0123-4567")]                  // punctuation
    public void Validate_InvalidAccountNumber_FailsWithInvalidMessage(string accountNumber)
    {
        _validator.TestValidate(Valid(accountNumber: accountNumber))
            .ShouldHaveValidationErrorFor(request => request.AccountNumber)
            .WithErrorMessage("Số tài khoản không hợp lệ.");
    }

    [Fact]
    public void Validate_EmptyAccountNumber_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(accountNumber: ""))
            .ShouldHaveValidationErrorFor(request => request.AccountNumber)
            .WithErrorMessage("Số tài khoản không được để trống.");
    }

    [Fact]
    public void Validate_EmptyBankName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(bankName: ""))
            .ShouldHaveValidationErrorFor(request => request.BankName)
            .WithErrorMessage("Tên ngân hàng không được để trống.");
    }

    [Fact]
    public void Validate_BankName100Chars_Passes()
    {
        _validator.TestValidate(Valid(bankName: new string('a', 100))).ShouldNotHaveValidationErrorFor(request => request.BankName);
    }

    [Fact]
    public void Validate_BankNameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(bankName: new string('a', 101)))
            .ShouldHaveValidationErrorFor(request => request.BankName)
            .WithErrorMessage("Tên ngân hàng không được vượt quá 100 ký tự.");
    }

    [Fact]
    public void Validate_EmptyAccountHolderName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(accountHolderName: ""))
            .ShouldHaveValidationErrorFor(request => request.AccountHolderName)
            .WithErrorMessage("Tên chủ tài khoản không được để trống.");
    }

    [Fact]
    public void Validate_AccountHolderNameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(accountHolderName: new string('a', 101)))
            .ShouldHaveValidationErrorFor(request => request.AccountHolderName)
            .WithErrorMessage("Tên chủ tài khoản không được vượt quá 100 ký tự.");
    }
}

public class UpdateBankAccountRequestValidatorTests
{
    private readonly UpdateBankAccountRequestValidator _validator = new();

    private static UpdateBankAccountRequest Valid(
        string bankBin = "970436",
        string bankName = "Vietcombank",
        string accountNumber = "0123456789",
        string accountHolderName = "Nguyen Van A") =>
        new() { BankBin = bankBin, BankName = bankName, AccountNumber = accountNumber, AccountHolderName = accountHolderName };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NonSixDigitBankBin_FailsWithFormatMessage()
    {
        _validator.TestValidate(Valid(bankBin: "1234567"))
            .ShouldHaveValidationErrorFor(request => request.BankBin)
            .WithErrorMessage("Mã ngân hàng (BIN) phải gồm đúng 6 chữ số.");
    }

    [Fact]
    public void Validate_InvalidAccountNumber_FailsWithInvalidMessage()
    {
        _validator.TestValidate(Valid(accountNumber: "abc"))
            .ShouldHaveValidationErrorFor(request => request.AccountNumber)
            .WithErrorMessage("Số tài khoản không hợp lệ.");
    }

    [Fact]
    public void Validate_BankNameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(bankName: new string('a', 101)))
            .ShouldHaveValidationErrorFor(request => request.BankName)
            .WithErrorMessage("Tên ngân hàng không được vượt quá 100 ký tự.");
    }

    [Fact]
    public void Validate_AccountHolderNameOver100Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(accountHolderName: new string('a', 101)))
            .ShouldHaveValidationErrorFor(request => request.AccountHolderName)
            .WithErrorMessage("Tên chủ tài khoản không được vượt quá 100 ký tự.");
    }
}
