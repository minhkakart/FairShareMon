using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Validators.Admin;
using FluentValidation.TestHelper;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for the M11 admin request validators. Grant amount must be <c>&gt;= 0</c>
/// (0 = comp) and currency/reference/note honour the entity max lengths; revoke only length-limits its
/// note; the revenue/metrics validators enforce <c>From &lt;= To</c> only when both bounds are present
/// and pin <c>bucket ∈ {day, month}</c>; the user-list validator caps page/pageSize and whitelists
/// sort/direction; the role validator whitelists <c>{USER, ADMIN}</c>; the reset-password validator
/// reuses the register password policy. All failures are the 1001 ValidationFailed path; the camelCase
/// <c>error.fields</c> keys are covered end-to-end by the endpoint tests.
/// </summary>
public class AdminValidatorsTests
{
    private static readonly DateTime From = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);

    // ---- GrantTierRequestValidator (OQ15) ---------------------------------------------------------

    private readonly GrantTierRequestValidator _grant = new();

    [Fact]
    public void Grant_AmountZero_Passes() =>
        _grant.TestValidate(new GrantTierRequest { Amount = 0m }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Grant_AmountPositive_Passes() =>
        _grant.TestValidate(new GrantTierRequest { Amount = 199_000m, Currency = "VND", Reference = "TT01", Note = "ok" })
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Grant_AmountNegative_Fails() =>
        _grant.TestValidate(new GrantTierRequest { Amount = -1m }).ShouldHaveValidationErrorFor(request => request.Amount);

    [Fact]
    public void Grant_CurrencyTooLong_Fails() =>
        _grant.TestValidate(new GrantTierRequest { Amount = 1m, Currency = "VNDX" })
            .ShouldHaveValidationErrorFor(request => request.Currency);

    [Fact]
    public void Grant_NullCurrency_Passes() =>
        _grant.TestValidate(new GrantTierRequest { Amount = 1m, Currency = null }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Grant_ReferenceTooLong_Fails() =>
        _grant.TestValidate(new GrantTierRequest { Amount = 1m, Reference = new string('x', TierGrant.ReferenceMaxLength + 1) })
            .ShouldHaveValidationErrorFor(request => request.Reference);

    [Fact]
    public void Grant_NoteTooLong_Fails() =>
        _grant.TestValidate(new GrantTierRequest { Amount = 1m, Note = new string('x', TierGrant.NoteMaxLength + 1) })
            .ShouldHaveValidationErrorFor(request => request.Note);

    // ---- RevokeTierRequestValidator ---------------------------------------------------------------

    private readonly RevokeTierRequestValidator _revoke = new();

    [Fact]
    public void Revoke_EmptyNote_Passes() =>
        _revoke.TestValidate(new RevokeTierRequest()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Revoke_NoteTooLong_Fails() =>
        _revoke.TestValidate(new RevokeTierRequest { Note = new string('x', TierGrant.NoteMaxLength + 1) })
            .ShouldHaveValidationErrorFor(request => request.Note);

    // ---- RevenueRequestValidator (OQ14) -----------------------------------------------------------

    private readonly RevenueRequestValidator _revenue = new();

    [Fact]
    public void Revenue_DefaultMonthBucket_NoRange_Passes() =>
        _revenue.TestValidate(new RevenueRequest()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Revenue_DayBucket_Passes() =>
        _revenue.TestValidate(new RevenueRequest { Bucket = DashboardBuckets.Day }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Revenue_UnknownBucket_Fails() =>
        _revenue.TestValidate(new RevenueRequest { Bucket = "week" }).ShouldHaveValidationErrorFor(request => request.Bucket);

    [Fact]
    public void Revenue_FromAfterTo_Fails() =>
        _revenue.TestValidate(new RevenueRequest { From = To, To = From }).ShouldHaveValidationErrorFor(request => request.To);

    [Fact]
    public void Revenue_OnlyFrom_Passes() =>
        _revenue.TestValidate(new RevenueRequest { From = From }).ShouldNotHaveAnyValidationErrors();

    // ---- AdminMetricsRequestValidator (OQ6) -------------------------------------------------------

    private readonly AdminMetricsRequestValidator _metrics = new();

    [Fact]
    public void Metrics_FromAfterTo_Fails() =>
        _metrics.TestValidate(new AdminMetricsRequest { From = To, To = From }).ShouldHaveValidationErrorFor(request => request.To);

    [Fact]
    public void Metrics_UnknownBucket_Fails() =>
        _metrics.TestValidate(new AdminMetricsRequest { Bucket = "quarter" }).ShouldHaveValidationErrorFor(request => request.Bucket);

    [Fact]
    public void Metrics_BothBoundsOmitted_Passes() =>
        _metrics.TestValidate(new AdminMetricsRequest()).ShouldNotHaveAnyValidationErrors();

    // ---- AdminUserListRequestValidator (OQ7) ------------------------------------------------------

    private readonly AdminUserListRequestValidator _list = new();

    [Fact]
    public void List_Defaults_Pass() =>
        _list.TestValidate(new AdminUserListRequest()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void List_PageZero_Fails() =>
        _list.TestValidate(new AdminUserListRequest { Page = 0 }).ShouldHaveValidationErrorFor(request => request.Page);

    [Fact]
    public void List_PageSizeOverCap_Fails() =>
        _list.TestValidate(new AdminUserListRequest { PageSize = AdminUserListRequestValidator.MaxPageSize + 1 })
            .ShouldHaveValidationErrorFor(request => request.PageSize);

    [Fact]
    public void List_PageSizeZero_Fails() =>
        _list.TestValidate(new AdminUserListRequest { PageSize = 0 }).ShouldHaveValidationErrorFor(request => request.PageSize);

    [Fact]
    public void List_UnknownSort_Fails() =>
        _list.TestValidate(new AdminUserListRequest { Sort = "email" }).ShouldHaveValidationErrorFor(request => request.Sort);

    [Fact]
    public void List_UnknownDirection_Fails() =>
        _list.TestValidate(new AdminUserListRequest { Direction = "up" }).ShouldHaveValidationErrorFor(request => request.Direction);

    [Theory]
    [InlineData("createdAt")]
    [InlineData("username")]
    [InlineData("tier")]
    [InlineData("status")]
    public void List_AllowedSorts_Pass(string sort) =>
        _list.TestValidate(new AdminUserListRequest { Sort = sort }).ShouldNotHaveAnyValidationErrors();

    // ---- SetRoleRequestValidator (OQ9) ------------------------------------------------------------

    private readonly SetRoleRequestValidator _role = new();

    [Theory]
    [InlineData(UserRoles.User)]
    [InlineData(UserRoles.Admin)]
    public void Role_Allowed_Pass(string role) =>
        _role.TestValidate(new SetRoleRequest { Role = role }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Role_Empty_Fails() =>
        _role.TestValidate(new SetRoleRequest { Role = string.Empty }).ShouldHaveValidationErrorFor(request => request.Role);

    [Fact]
    public void Role_Unknown_Fails() =>
        _role.TestValidate(new SetRoleRequest { Role = "ROOT" }).ShouldHaveValidationErrorFor(request => request.Role);

    // ---- ResetPasswordRequestValidator (OQ8) ------------------------------------------------------

    private readonly ResetPasswordRequestValidator _reset = new();

    [Fact]
    public void Reset_ValidPassword_Passes() =>
        _reset.TestValidate(new ResetPasswordRequest { NewPassword = "password-8+" }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Reset_TooShort_Fails() =>
        _reset.TestValidate(new ResetPasswordRequest { NewPassword = "short" }).ShouldHaveValidationErrorFor(request => request.NewPassword);

    [Fact]
    public void Reset_Empty_Fails() =>
        _reset.TestValidate(new ResetPasswordRequest { NewPassword = string.Empty }).ShouldHaveValidationErrorFor(request => request.NewPassword);
}
