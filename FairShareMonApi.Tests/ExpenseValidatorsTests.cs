using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Validators.Expenses;
using FairShareMonApi.Validators.Shares;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the expense create validator (OQ16): <c>Name</c> required (whitespace-only
/// rejected) + max 200; <c>Description</c> optional + max 1000; <c>ExpenseTime</c> required; each
/// submitted share's <c>MemberUuid</c> required, <c>Amount</c> non-negative (§4.3), <c>Note</c> max
/// 500. Messages are pinned per the Step-9 list; the camelCase <c>error.fields</c> keys are covered
/// end-to-end by the endpoint tests.
/// </summary>
public class CreateExpenseRequestValidatorTests
{
    private readonly CreateExpenseRequestValidator _validator = new();

    private static CreateExpenseRequest Valid(
        string? name = "Ăn trưa",
        string? description = "Cơm văn phòng",
        DateTime? expenseTime = null,
        IReadOnlyList<CreateShareInput>? shares = null) =>
        new()
        {
            Name = name!,
            Description = description,
            ExpenseTime = expenseTime ?? new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc),
            Shares = shares
        };

    private static CreateShareInput Share(string memberUuid = "m-1", decimal amount = 100_000m, string? note = null) =>
        new() { MemberUuid = memberUuid, Amount = amount, Note = note };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid(shares: [Share()])).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullShares_Passes()
    {
        _validator.TestValidate(Valid(shares: null)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(name: ""))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên phiếu chi tiêu không được để trống.");
    }

    [Fact]
    public void Validate_WhitespaceOnlyName_FailsRequired()
    {
        _validator.TestValidate(Valid(name: "   "))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên phiếu chi tiêu không được để trống.");
    }

    [Fact]
    public void Validate_Name200Chars_Passes()
    {
        _validator.TestValidate(Valid(name: new string('a', 200)))
            .ShouldNotHaveValidationErrorFor(request => request.Name);
    }

    [Fact]
    public void Validate_NameOver200Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(name: new string('a', 201)))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên phiếu chi tiêu không được vượt quá 200 ký tự.");
    }

    [Fact]
    public void Validate_NullDescription_Passes()
    {
        _validator.TestValidate(Valid(description: null))
            .ShouldNotHaveValidationErrorFor(request => request.Description);
    }

    [Fact]
    public void Validate_Description1000Chars_Passes()
    {
        _validator.TestValidate(Valid(description: new string('x', 1000)))
            .ShouldNotHaveValidationErrorFor(request => request.Description);
    }

    [Fact]
    public void Validate_DescriptionOver1000Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(description: new string('x', 1001)))
            .ShouldHaveValidationErrorFor(request => request.Description)
            .WithErrorMessage("Mô tả không được vượt quá 1000 ký tự.");
    }

    [Fact]
    public void Validate_DefaultExpenseTime_FailsRequired()
    {
        _validator.TestValidate(Valid(expenseTime: DateTime.MinValue))
            .ShouldHaveValidationErrorFor(request => request.ExpenseTime)
            .WithErrorMessage("Thời điểm chi không được để trống.");
    }

    [Fact]
    public void Validate_ShareWithEmptyMemberUuid_Fails()
    {
        _validator.TestValidate(Valid(shares: [Share(memberUuid: "")]))
            .ShouldHaveValidationErrorFor("Shares[0].MemberUuid")
            .WithErrorMessage("Thành viên của phần gánh không được để trống.");
    }

    [Fact]
    public void Validate_ShareWithNegativeAmount_FailsWithNonNegativeMessage()
    {
        _validator.TestValidate(Valid(shares: [Share(amount: -1m)]))
            .ShouldHaveValidationErrorFor("Shares[0].Amount")
            .WithErrorMessage("Số tiền không được âm.");
    }

    [Fact]
    public void Validate_ShareWithZeroAmount_Passes()
    {
        _validator.TestValidate(Valid(shares: [Share(amount: 0m)]))
            .ShouldNotHaveValidationErrorFor("Shares[0].Amount");
    }

    [Fact]
    public void Validate_ShareNoteOver500Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(shares: [Share(note: new string('n', 501))]))
            .ShouldHaveValidationErrorFor("Shares[0].Note")
            .WithErrorMessage("Ghi chú không được vượt quá 500 ký tự.");
    }

    [Fact]
    public void Validate_ShareNote500Chars_Passes()
    {
        _validator.TestValidate(Valid(shares: [Share(note: new string('n', 500))]))
            .ShouldNotHaveValidationErrorFor("Shares[0].Note");
    }
}

/// <summary>Update-general-info validator: same field policy as create minus shares (OQ16).</summary>
public class UpdateExpenseRequestValidatorTests
{
    private readonly UpdateExpenseRequestValidator _validator = new();

    private static UpdateExpenseRequest Valid(string? name = "Ăn trưa", string? description = null, DateTime? expenseTime = null) =>
        new()
        {
            Name = name!,
            Description = description,
            ExpenseTime = expenseTime ?? new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc)
        };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(name: ""))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên phiếu chi tiêu không được để trống.");
    }

    [Fact]
    public void Validate_NameOver200Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(name: new string('a', 201)))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên phiếu chi tiêu không được vượt quá 200 ký tự.");
    }

    [Fact]
    public void Validate_DescriptionOver1000Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(description: new string('x', 1001)))
            .ShouldHaveValidationErrorFor(request => request.Description)
            .WithErrorMessage("Mô tả không được vượt quá 1000 ký tự.");
    }

    [Fact]
    public void Validate_DefaultExpenseTime_FailsRequired()
    {
        _validator.TestValidate(Valid(expenseTime: DateTime.MinValue))
            .ShouldHaveValidationErrorFor(request => request.ExpenseTime)
            .WithErrorMessage("Thời điểm chi không được để trống.");
    }
}

/// <summary>Share add/update validators: <c>MemberUuid</c> required; <c>Amount</c> ≥ 0; <c>Note</c> max 500.</summary>
public class CreateShareRequestValidatorTests
{
    private readonly CreateShareRequestValidator _validator = new();

    private static CreateShareRequest Valid(string memberUuid = "m-1", decimal amount = 50_000m, string? note = null) =>
        new() { MemberUuid = memberUuid, Amount = amount, Note = note };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyMemberUuid_FailsRequired()
    {
        _validator.TestValidate(Valid(memberUuid: ""))
            .ShouldHaveValidationErrorFor(request => request.MemberUuid)
            .WithErrorMessage("Thành viên của phần gánh không được để trống.");
    }

    [Fact]
    public void Validate_NegativeAmount_FailsWithNonNegativeMessage()
    {
        _validator.TestValidate(Valid(amount: -0.01m))
            .ShouldHaveValidationErrorFor(request => request.Amount)
            .WithErrorMessage("Số tiền không được âm.");
    }

    [Fact]
    public void Validate_ZeroAmount_Passes()
    {
        _validator.TestValidate(Valid(amount: 0m)).ShouldNotHaveValidationErrorFor(request => request.Amount);
    }

    [Fact]
    public void Validate_NoteOver500Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(note: new string('n', 501)))
            .ShouldHaveValidationErrorFor(request => request.Note)
            .WithErrorMessage("Ghi chú không được vượt quá 500 ký tự.");
    }
}

public class UpdateShareRequestValidatorTests
{
    private readonly UpdateShareRequestValidator _validator = new();

    private static UpdateShareRequest Valid(string memberUuid = "m-1", decimal amount = 50_000m, string? note = null) =>
        new() { MemberUuid = memberUuid, Amount = amount, Note = note };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyMemberUuid_FailsRequired()
    {
        _validator.TestValidate(Valid(memberUuid: ""))
            .ShouldHaveValidationErrorFor(request => request.MemberUuid)
            .WithErrorMessage("Thành viên của phần gánh không được để trống.");
    }

    [Fact]
    public void Validate_NegativeAmount_FailsWithNonNegativeMessage()
    {
        _validator.TestValidate(Valid(amount: -1m))
            .ShouldHaveValidationErrorFor(request => request.Amount)
            .WithErrorMessage("Số tiền không được âm.");
    }

    [Fact]
    public void Validate_NoteOver500Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(note: new string('n', 501)))
            .ShouldHaveValidationErrorFor(request => request.Note)
            .WithErrorMessage("Ghi chú không được vượt quá 500 ký tự.");
    }
}
