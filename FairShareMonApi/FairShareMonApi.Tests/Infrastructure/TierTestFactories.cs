using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// A test host whose <c>Tiers:Free:</c> limits are overridden LOW (members / open events /
/// expenses-per-month = 2) via an in-memory config source appended after appsettings.json (so it wins).
/// Lets the M10 endpoint tests hit the create-limits with just a few rows instead of 25/10/200. The
/// committed appsettings.json is never touched - the override lives only in this factory.
/// </summary>
public sealed class TierLimitWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tiers:Free:MaxMembers"] = "2",
            ["Tiers:Free:MaxOpenEvents"] = "2",
            ["Tiers:Free:MaxExpensesPerMonth"] = "2"
        }));
}

/// <summary>
/// A test host whose <c>Tiers:Free:MaxMembers</c> is overridden to <c>0</c>. Proves the owner-rep
/// bootstrap is exempt from the create-guard: registration still yields the owner-representative member
/// even though the guarded <c>POST /members</c> is blocked at 0.
/// </summary>
public sealed class ZeroMemberLimitWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tiers:Free:MaxMembers"] = "0"
        }));
}
