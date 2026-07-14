using FairShareMonApi.Models.Stats;
using FairShareMonApi.Validators.Stats;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the M7 stats range validators (no DB). <see cref="StatsRangeRequestValidator"/>
/// (overview) and <see cref="ByCategoryStatsRequestValidator"/> both enforce the OQ7 rule
/// <c>From &lt;= To</c> only when BOTH bounds are present (either bound alone, or neither, is valid =
/// all-time). The by-category validator adds the OQ8 mutual-exclusion rule: <c>EventUuid</c> may not be
/// sent together with a time range. Both are 1001 validation failures with the pinned Vietnamese
/// messages; the camelCase <c>error.fields</c> keys are covered end-to-end by the endpoint tests.
/// </summary>
public class StatsRangeRequestValidatorTests
{
    private const string RangeMessage =
        "Khoảng thời gian không hợp lệ: thời điểm bắt đầu phải trước hoặc bằng thời điểm kết thúc.";

    private readonly StatsRangeRequestValidator _validator = new();

    private static readonly DateTime From = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Validate_FromBeforeTo_Passes()
    {
        _validator.TestValidate(new StatsRangeRequest { From = From, To = To }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_FromEqualsTo_Passes()
    {
        _validator.TestValidate(new StatsRangeRequest { From = From, To = From }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BothBoundsOmitted_Passes()
    {
        // Omit = all-time (OQ7).
        _validator.TestValidate(new StatsRangeRequest()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_OnlyFrom_Passes()
    {
        _validator.TestValidate(new StatsRangeRequest { From = From }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_OnlyTo_Passes()
    {
        _validator.TestValidate(new StatsRangeRequest { To = To }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_FromAfterTo_FailsWithRangeMessage()
    {
        _validator.TestValidate(new StatsRangeRequest { From = To, To = From })
            .ShouldHaveValidationErrorFor(request => request.To)
            .WithErrorMessage(RangeMessage);
    }
}

/// <summary>By-category scope validator (OQ7 range rule + OQ8 mutual exclusion).</summary>
public class ByCategoryStatsRequestValidatorTests
{
    private const string RangeMessage =
        "Khoảng thời gian không hợp lệ: thời điểm bắt đầu phải trước hoặc bằng thời điểm kết thúc.";

    private const string BothScopesMessage =
        "Chỉ được lọc theo đợt hoặc theo khoảng thời gian, không dùng đồng thời.";

    private readonly ByCategoryStatsRequestValidator _validator = new();

    private static readonly DateTime From = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Validate_TimeRangeOnly_Passes()
    {
        _validator.TestValidate(new ByCategoryStatsRequest { From = From, To = To }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EventUuidOnly_Passes()
    {
        _validator.TestValidate(new ByCategoryStatsRequest { EventUuid = "evt-1" }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NoScope_Passes()
    {
        _validator.TestValidate(new ByCategoryStatsRequest()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_FromAfterTo_FailsWithRangeMessage()
    {
        _validator.TestValidate(new ByCategoryStatsRequest { From = To, To = From })
            .ShouldHaveValidationErrorFor(request => request.To)
            .WithErrorMessage(RangeMessage);
    }

    [Fact]
    public void Validate_EventUuidWithFrom_FailsWithBothScopesMessage()
    {
        _validator.TestValidate(new ByCategoryStatsRequest { EventUuid = "evt-1", From = From })
            .ShouldHaveValidationErrorFor(request => request.EventUuid)
            .WithErrorMessage(BothScopesMessage);
    }

    [Fact]
    public void Validate_EventUuidWithTo_FailsWithBothScopesMessage()
    {
        _validator.TestValidate(new ByCategoryStatsRequest { EventUuid = "evt-1", To = To })
            .ShouldHaveValidationErrorFor(request => request.EventUuid)
            .WithErrorMessage(BothScopesMessage);
    }

    [Fact]
    public void Validate_EventUuidWithFullRange_FailsWithBothScopesMessage()
    {
        _validator.TestValidate(new ByCategoryStatsRequest { EventUuid = "evt-1", From = From, To = To })
            .ShouldHaveValidationErrorFor(request => request.EventUuid)
            .WithErrorMessage(BothScopesMessage);
    }
}
