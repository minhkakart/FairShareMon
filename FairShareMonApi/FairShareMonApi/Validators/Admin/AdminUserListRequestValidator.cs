using FairShareMonApi.Models.Admin;
using FairShareMonApi.Constants;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using Microsoft.Extensions.Localization;
using FluentValidation;

namespace FairShareMonApi.Validators.Admin;

/// <summary>
/// User-listing rules (M11 OQ7): <c>Page &gt;= 1</c>; <c>PageSize</c> trong [1, 100]; <c>Sort</c> thuộc
/// {createdAt, username, tier, status}; <c>Direction</c> thuộc {asc, desc}. Sai -&gt; 1001.
/// </summary>
public class AdminUserListRequestValidator : AbstractValidator<AdminUserListRequest>
{
    public const int MaxPageSize = 100;

    private static readonly string[] AllowedSorts = ["createdAt", "username", "tier", "status"];
    private static readonly string[] AllowedDirections = ["asc", "desc"];

    public AdminUserListRequestValidator(IStringLocalizer<StringResources>? localizer = null)
    {
        localizer ??= SharedStringLocalizer.Instance;
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1).WithMessage(_ => localizer[MessageKeys.Validation.Admin.PageMin].Value);

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage(_ => localizer[MessageKeys.Validation.Admin.PageSizeRange, MaxPageSize].Value);

        RuleFor(request => request.Sort)
            .Must(sort => AllowedSorts.Contains(sort))
            .WithMessage(_ => localizer[MessageKeys.Validation.Admin.SortInvalid].Value);

        RuleFor(request => request.Direction)
            .Must(direction => AllowedDirections.Contains(direction))
            .WithMessage(_ => localizer[MessageKeys.Validation.Admin.DirectionInvalid].Value);
    }
}
