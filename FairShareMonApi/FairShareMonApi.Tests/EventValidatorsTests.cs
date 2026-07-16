using FairShareMonApi.Models.Events;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Validators.Events;
using FairShareMonApi.Validators.Expenses;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the event create validator (OQ9/OQ1): <c>Name</c> required (whitespace-only
/// rejected) + max 200; <c>Description</c> optional + max 1000; <c>StartDate</c>/<c>EndDate</c> required
/// (non-default); <c>EndDate &gt;= StartDate</c> on the calendar day → 1001 with the pinned Vietnamese
/// message (distinct from the 9xxx business codes and the DB CHECK). The camelCase <c>error.fields</c>
/// keys are covered end-to-end by the endpoint tests.
/// </summary>
[UseCulture("vi-VN")]
public class CreateEventRequestValidatorTests
{
    private readonly CreateEventRequestValidator _validator = new();

    private static readonly DateTime Start = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static CreateEventRequest Valid(
        string? name = "Đà Lạt 3 ngày",
        string? description = "Chuyến đi công ty",
        DateTime? startDate = null,
        DateTime? endDate = null) =>
        new()
        {
            Name = name!,
            Description = description,
            StartDate = startDate ?? Start,
            EndDate = endDate ?? End
        };

    [Fact]
    public void Validate_AllFieldsValid_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NullDescription_Passes()
    {
        _validator.TestValidate(Valid(description: null)).ShouldNotHaveValidationErrorFor(request => request.Description);
    }

    [Fact]
    public void Validate_EmptyName_FailsWithRequiredMessage()
    {
        _validator.TestValidate(Valid(name: ""))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên đợt không được để trống.");
    }

    [Fact]
    public void Validate_WhitespaceOnlyName_FailsRequired()
    {
        _validator.TestValidate(Valid(name: "   "))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên đợt không được để trống.");
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
            .WithErrorMessage("Tên đợt không được vượt quá 200 ký tự.");
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
            .WithErrorMessage("Mô tả đợt không được vượt quá 1000 ký tự.");
    }

    [Fact]
    public void Validate_DefaultStartDate_FailsRequired()
    {
        _validator.TestValidate(Valid(startDate: DateTime.MinValue))
            .ShouldHaveValidationErrorFor(request => request.StartDate)
            .WithErrorMessage("Ngày bắt đầu không được để trống.");
    }

    [Fact]
    public void Validate_DefaultEndDate_FailsRequired()
    {
        // MinValue end + a valid start also trips the ordering rule; assert the required rule fired.
        _validator.TestValidate(Valid(startDate: DateTime.MinValue, endDate: DateTime.MinValue))
            .ShouldHaveValidationErrorFor(request => request.EndDate)
            .WithErrorMessage("Ngày kết thúc không được để trống.");
    }

    [Fact]
    public void Validate_EndDateBeforeStartDate_FailsWithOrderMessage()
    {
        _validator.TestValidate(Valid(startDate: End, endDate: Start))
            .ShouldHaveValidationErrorFor(request => request.EndDate)
            .WithErrorMessage("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
    }

    [Fact]
    public void Validate_EndDateEqualsStartDate_Passes()
    {
        // A single-day event is valid (whole-day inclusive window, OQ1).
        _validator.TestValidate(Valid(startDate: Start, endDate: Start))
            .ShouldNotHaveValidationErrorFor(request => request.EndDate);
    }

    [Fact]
    public void Validate_EndDateSameDayEarlierClockTime_Passes()
    {
        // The comparison is on the calendar day, so an earlier clock time on the same day is fine.
        _validator.TestValidate(Valid(
                startDate: new DateTime(2026, 7, 14, 20, 0, 0, DateTimeKind.Utc),
                endDate: new DateTime(2026, 7, 14, 6, 0, 0, DateTimeKind.Utc)))
            .ShouldNotHaveValidationErrorFor(request => request.EndDate);
    }
}

/// <summary>Event update validator: same field policy as create (OQ9/OQ1).</summary>
[UseCulture("vi-VN")]
public class UpdateEventRequestValidatorTests
{
    private readonly UpdateEventRequestValidator _validator = new();

    private static readonly DateTime Start = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static UpdateEventRequest Valid(
        string? name = "Đà Lạt 3 ngày",
        string? description = null,
        DateTime? startDate = null,
        DateTime? endDate = null) =>
        new()
        {
            Name = name!,
            Description = description,
            StartDate = startDate ?? Start,
            EndDate = endDate ?? End
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
            .WithErrorMessage("Tên đợt không được để trống.");
    }

    [Fact]
    public void Validate_NameOver200Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(name: new string('a', 201)))
            .ShouldHaveValidationErrorFor(request => request.Name)
            .WithErrorMessage("Tên đợt không được vượt quá 200 ký tự.");
    }

    [Fact]
    public void Validate_DescriptionOver1000Chars_FailsWithMaxLengthMessage()
    {
        _validator.TestValidate(Valid(description: new string('x', 1001)))
            .ShouldHaveValidationErrorFor(request => request.Description)
            .WithErrorMessage("Mô tả đợt không được vượt quá 1000 ký tự.");
    }

    [Fact]
    public void Validate_EndDateBeforeStartDate_FailsWithOrderMessage()
    {
        _validator.TestValidate(Valid(startDate: End, endDate: Start))
            .ShouldHaveValidationErrorFor(request => request.EndDate)
            .WithErrorMessage("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
    }

    [Fact]
    public void Validate_EndDateEqualsStartDate_Passes()
    {
        _validator.TestValidate(Valid(startDate: Start, endDate: Start))
            .ShouldNotHaveValidationErrorFor(request => request.EndDate);
    }
}

/// <summary>Assign-to-event validator (OQ4): <c>EventUuid</c> required.</summary>
[UseCulture("vi-VN")]
public class AssignEventRequestValidatorTests
{
    private readonly AssignEventRequestValidator _validator = new();

    [Fact]
    public void Validate_NonEmptyEventUuid_Passes()
    {
        _validator.TestValidate(new AssignEventRequest { EventUuid = "evt-1" }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyEventUuid_FailsRequired()
    {
        _validator.TestValidate(new AssignEventRequest { EventUuid = "" })
            .ShouldHaveValidationErrorFor(request => request.EventUuid)
            .WithErrorMessage("UUID đợt chi tiêu không được để trống.");
    }
}
