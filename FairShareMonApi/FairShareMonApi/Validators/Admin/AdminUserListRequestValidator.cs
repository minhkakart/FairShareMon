using FairShareMonApi.Models.Admin;
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

    public AdminUserListRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Số trang phải lớn hơn hoặc bằng 1.");

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"Kích thước trang phải trong khoảng 1 đến {MaxPageSize}.");

        RuleFor(request => request.Sort)
            .Must(sort => AllowedSorts.Contains(sort))
            .WithMessage("Trường sắp xếp không hợp lệ. Chỉ chấp nhận createdAt, username, tier hoặc status.");

        RuleFor(request => request.Direction)
            .Must(direction => AllowedDirections.Contains(direction))
            .WithMessage("Chiều sắp xếp không hợp lệ. Chỉ chấp nhận asc hoặc desc.");
    }
}
