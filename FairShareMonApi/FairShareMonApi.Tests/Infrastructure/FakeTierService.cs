using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Services.Api.Tiers;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Pass-through <see cref="ITierService"/> test double for the pure service unit tests (no DB). By
/// default every guard/gate is a no-op (mirrors a Premium caller / an under-limit Free caller), so the
/// existing service tests that only exercise the happy path keep passing unchanged. Set one of the
/// <c>*Code</c> properties to simulate a Free-tier breach; the matching method then throws that
/// 13xxx <see cref="ErrorException"/> so a test can prove the create service surfaces it verbatim.
/// </summary>
public sealed class FakeTierService : ITierService
{
    /// <summary>When set, <see cref="EnsureCanCreateMemberAsync"/> throws this code (e.g. 13000).</summary>
    public int? MemberLimitCode { get; set; }

    /// <summary>When set, <see cref="EnsureCanCreateOpenEventAsync"/> throws this code (e.g. 13001).</summary>
    public int? OpenEventLimitCode { get; set; }

    /// <summary>When set, <see cref="EnsureCanCreateExpenseAsync"/> throws this code (e.g. 13002).</summary>
    public int? ExpenseLimitCode { get; set; }

    /// <summary>When set, <see cref="EnsurePremiumFeature"/> throws this code (e.g. 13003).</summary>
    public int? PremiumFeatureCode { get; set; }

    public Task EnsureCanCreateMemberAsync(string userUuid, CancellationToken cancellationToken = default) =>
        Guard(MemberLimitCode);

    public Task EnsureCanCreateOpenEventAsync(string userUuid, CancellationToken cancellationToken = default) =>
        Guard(OpenEventLimitCode);

    public Task EnsureCanCreateExpenseAsync(string userUuid, CancellationToken cancellationToken = default) =>
        Guard(ExpenseLimitCode);

    public void EnsurePremiumFeature(string featureNameVi)
    {
        if (PremiumFeatureCode is int code)
            throw new ErrorException(code, "premium-gated (test double)");
    }

    private static Task Guard(int? code) =>
        code is int value
            ? throw new ErrorException(value, "tier-limit (test double)")
            : Task.CompletedTask;
}
