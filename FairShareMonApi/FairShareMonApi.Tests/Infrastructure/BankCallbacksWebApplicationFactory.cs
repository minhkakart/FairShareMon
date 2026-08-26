using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Test host for the bank-callbacks endpoint (planning/bank-callback-settlement.md Step 10). This
/// feature makes NO outbound HTTP calls (Background) - unlike <see cref="BanksStubWebApplicationFactory"/>
/// there is nothing to stub - so the only override needed is a known, non-blank <c>BankCallbacks:SePay:
/// ApiKey</c> config value (the shipped default is intentionally blank, which fails closed per
/// <c>SePayBankCallbackParser.Verify</c>), so tests can exercise a real 200 webhook-accepted path.
/// </summary>
public sealed class BankCallbacksWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ConfiguredApiKey = "test-sepay-webhook-secret";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BankCallbacks:SePay:ApiKey"] = ConfiguredApiKey,
            ["BankCallbacks:SePay:CodePrefix"] = "FSM"
        }));
}
