using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the owner-facing share-link routes
/// <c>POST/GET/DELETE api/v1/events/{uuid}/share</c> via WebApplicationFactory (real MariaDB/Redis -
/// skippable). Proves the full <c>ApiResult</c> envelope + real status codes and business rules:
/// creation is Premium-gated (Free -&gt; 403 13003, gate fires BEFORE event resolution) and closed-only
/// (open event -&gt; 400 16001); an explicit bad bank -&gt; 404 12000; no wallet account -&gt; 200
/// <c>hasQr=false</c>; a second create reuses the same token; <c>regenerate</c> mints a new token and
/// 404s the old one on the public route; GET returns the active link or 200 <c>data:null</c> when not
/// shared; DELETE revokes (subsequent public GET -&gt; 404 16000) and is idempotent; ownership is scoped
/// (foreign/unknown event -&gt; 404 9000, never 403); anonymous -&gt; 401.
/// </summary>
[Collection("AuthIntegration")]
public class EventShareEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static async Task CreateBankAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Closed event where An (owner-rep) owes 200k and Bình advanced.</summary>
    private async Task<string> SeedClosedEventWithDebtorAsync(HttpClient client)
    {
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            eventUuid = evt,
            shares = new[]
            {
                new { memberUuid = an, amount = 200_000m },
                new { memberUuid = binh, amount = 300_000m }
            }
        });
        await CloseEventAsync(client, evt);
        return evt;
    }

    private static async Task<HttpResponseMessage> PostShareAsync(HttpClient client, string evt, object? body = null) =>
        await client.PostAsJsonAsync($"api/v1/events/{evt}/share", body ?? new { });

    private static async Task<(string Token, bool HasQr, JsonDocument Envelope)> CreateShareAsync(HttpClient client, string evt, object? body = null)
    {
        var response = await PostShareAsync(client, evt, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await ReadEnvelopeAsync(response);
        response.Dispose();
        var data = envelope.RootElement.GetProperty("data");
        return (data.GetProperty("token").GetString()!, data.GetProperty("hasQr").GetBoolean(), envelope);
    }

    // ---- POST create ------------------------------------------------------------------------------

    [SkippableFact]
    public async Task CreateShare_PremiumClosedEventWithBank_Returns200WithTokenAndHasQrTrue()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await SeedClosedEventWithDebtorAsync(client);

        using var response = await PostShareAsync(client, evt);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("token").GetString()));
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("expiresAt").ValueKind);
        Assert.True(data.GetProperty("hasQr").GetBoolean());
        Assert.Equal("Vietcombank", data.GetProperty("bankName").GetString());
    }

    [SkippableFact]
    public async Task CreateShare_NoWalletAccount_Returns200HasQrFalse()
    {
        using var client = await CreatePremiumClientAsync(); // no bank account configured
        var evt = await SeedClosedEventWithDebtorAsync(client);

        var (token, hasQr, envelope) = await CreateShareAsync(client, evt);
        using (envelope)
        {
            Assert.False(hasQr);
            Assert.False(string.IsNullOrWhiteSpace(token));
        }
    }

    [SkippableFact]
    public async Task CreateShare_FreeCaller_Returns403Code13003()
    {
        using var client = await CreateAuthorizedClientAsync(); // Free tier
        var evt = await SeedClosedEventWithDebtorAsync(client);

        using var response = await PostShareAsync(client, evt);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.PremiumFeatureRequired);
    }

    [SkippableFact]
    public async Task CreateShare_FreeCaller_GateFiresBeforeEventResolution_Returns403NotFound()
    {
        // A Free caller on a NON-EXISTENT event still gets 403 (gate first), never 404 - proves ordering.
        using var client = await CreateAuthorizedClientAsync();

        using var response = await PostShareAsync(client, "no-such-event");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.PremiumFeatureRequired);
    }

    [SkippableFact]
    public async Task CreateShare_OpenEvent_Returns400Code16001()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16); // not closed

        using var response = await PostShareAsync(client, evt);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotClosedForShare);
    }

    [SkippableFact]
    public async Task CreateShare_ExplicitBadBank_Returns404Code12000()
    {
        using var client = await CreatePremiumClientAsync();
        var evt = await SeedClosedEventWithDebtorAsync(client);

        using var response = await PostShareAsync(client, evt, new { bankAccountUuid = "no-such-account" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankAccountNotFound);
    }

    [SkippableFact]
    public async Task CreateShare_ExistingActiveLink_ReusesSameToken()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await SeedClosedEventWithDebtorAsync(client);

        var (first, _, env1) = await CreateShareAsync(client, evt);
        var (second, _, env2) = await CreateShareAsync(client, evt);
        using (env1) using (env2)
            Assert.Equal(first, second); // reuse, not a duplicate (Decision 4)
    }

    [SkippableFact]
    public async Task CreateShare_Regenerate_ReturnsNewTokenAnd404sOldOnPublicRoute()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await SeedClosedEventWithDebtorAsync(client);

        var (oldToken, _, env1) = await CreateShareAsync(client, evt);
        var (newToken, _, env2) = await CreateShareAsync(client, evt, new { regenerate = true });
        using (env1) using (env2)
            Assert.NotEqual(oldToken, newToken);

        // The old token no longer resolves on the anonymous public route.
        using var anonymous = Factory.CreateClient();
        using var oldPublic = await anonymous.GetAsync($"api/v1/public/shares/{oldToken}");
        Assert.Equal(HttpStatusCode.NotFound, oldPublic.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(oldPublic), ErrorCodes.ShareLinkNotFoundOrExpired);

        // The new token DOES resolve.
        using var newPublic = await anonymous.GetAsync($"api/v1/public/shares/{newToken}");
        Assert.Equal(HttpStatusCode.OK, newPublic.StatusCode);
    }

    [SkippableFact]
    public async Task CreateShare_ForeignEvent_Returns404Code9000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        var evt = await SeedClosedEventWithDebtorAsync(owner);

        using var response = await PostShareAsync(stranger, evt);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task CreateShare_UnknownEvent_Returns404Code9000()
    {
        using var client = await CreatePremiumClientAsync();

        using var response = await PostShareAsync(client, "no-such-event");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task CreateShare_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await PostShareAsync(client, "some-event");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    // ---- GET active -------------------------------------------------------------------------------

    [SkippableFact]
    public async Task GetShare_ActiveLink_Returns200WithSameToken()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await SeedClosedEventWithDebtorAsync(client);
        var (token, _, env) = await CreateShareAsync(client, evt);
        env.Dispose();

        using var response = await client.GetAsync($"api/v1/events/{evt}/share");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.Equal(token, envelope.RootElement.GetProperty("data").GetProperty("token").GetString());
    }

    [SkippableFact]
    public async Task GetShare_NotShared_Returns200DataNull()
    {
        using var client = await CreatePremiumClientAsync();
        var evt = await SeedClosedEventWithDebtorAsync(client);

        using var response = await client.GetAsync($"api/v1/events/{evt}/share");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // "not shared yet" is a normal state (OQ8a)
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Null, envelope.RootElement.GetProperty("data").ValueKind);
    }

    [SkippableFact]
    public async Task GetShare_ForeignEvent_Returns404Code9000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        var evt = await SeedClosedEventWithDebtorAsync(owner);

        using var response = await stranger.GetAsync($"api/v1/events/{evt}/share");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task GetShare_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("api/v1/events/some-event/share");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    // ---- DELETE revoke ----------------------------------------------------------------------------

    [SkippableFact]
    public async Task DeleteShare_Revokes_SubsequentPublicGetReturns404Code16000()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await SeedClosedEventWithDebtorAsync(client);
        var (token, _, env) = await CreateShareAsync(client, evt);
        env.Dispose();

        using (var delete = await client.DeleteAsync($"api/v1/events/{evt}/share"))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
            using var deleteEnvelope = await ReadEnvelopeAsync(delete);
            Assert.True(deleteEnvelope.RootElement.GetProperty("isSuccess").GetBoolean());
        }

        using var anonymous = Factory.CreateClient();
        using var publicGet = await anonymous.GetAsync($"api/v1/public/shares/{token}");
        Assert.Equal(HttpStatusCode.NotFound, publicGet.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(publicGet), ErrorCodes.ShareLinkNotFoundOrExpired);
    }

    [SkippableFact]
    public async Task DeleteShare_NoActiveLink_IsIdempotentSuccess()
    {
        using var client = await CreatePremiumClientAsync();
        var evt = await SeedClosedEventWithDebtorAsync(client);

        using var response = await client.DeleteAsync($"api/v1/events/{evt}/share");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // idempotent: no link = no-op success
    }

    [SkippableFact]
    public async Task DeleteShare_ForeignEvent_Returns404Code9000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        var evt = await SeedClosedEventWithDebtorAsync(owner);

        using var response = await stranger.DeleteAsync($"api/v1/events/{evt}/share");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task DeleteShare_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.DeleteAsync("api/v1/events/some-event/share");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            using var scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userIds = await context.Users
                .Where(user => user.Username.StartsWith(UsernamePrefix))
                .Select(user => user.Id)
                .ToListAsync();
            await context.EventShareLinks.Where(link => userIds.Contains(link.UserId)).ExecuteDeleteAsync();
            await context.BankAccounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
