using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Services.Api.Share;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the anonymous live-update stream
/// <c>GET api/v1/public/shares/{token}/stream</c> (planning/public-share-sse-updates.md) via
/// WebApplicationFactory (real MariaDB/Redis - skippable), mirroring <see cref="PublicShareEndpointTests"/>'s
/// seeding helpers. Uses <see cref="SseTestClient"/> - a genuinely new technique for this suite: every
/// existing endpoint test does a single request/response round trip, these hold a streaming response
/// open and read it incrementally, with every read bounded by an explicit timeout so a regression hangs
/// the TEST, never the whole run. Covers pre-stream token validation (still plain 404 JSON, never a half
/// -opened stream), the initial handshake, each of the three settled-mutation routes pushing
/// <c>event: updated</c> to an open stream on the SAME event (and staying silent for one with no active
/// link), owner revoke/regenerate pushing <c>event: revoked</c> and ending the stream, the heartbeat
/// keep-alive comment and heartbeat-driven natural-expiry <c>event: expired</c> (both with
/// <c>Share:StreamHeartbeatSeconds</c> overridden small via <c>WithWebHostBuilder</c>), and fan-out to
/// two concurrent subscribers on the same token.
/// </summary>
[Collection("AuthIntegration")]
public class PublicShareStreamEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Bound for reads that are expected to succeed quickly (an explicit publish already
    /// happened, or the connection should already be terminated) - generous enough to absorb CI jitter
    /// without masking a genuine regression as a slow pass.</summary>
    private static readonly TimeSpan ShortBound = TimeSpan.FromSeconds(5);

    /// <summary>Bound for a deliberate "nothing should arrive" negative assertion - short because we are
    /// NOT waiting out a heartbeat tick (default 20s), just proving no immediate false-positive signal.</summary>
    private static readonly TimeSpan NegativeBound = TimeSpan.FromSeconds(3);

    // ---- Seeding helpers, mirroring PublicShareEndpointTests.cs -------------------------------------

    private static async Task CreateBankAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string ShareUuidForMember(JsonElement expense, string memberUuid) =>
        expense.GetProperty("shares").EnumerateArray()
            .Single(share => share.GetProperty("member").GetProperty("uuid").GetString() == memberUuid)
            .GetProperty("uuid").GetString()!;

    /// <summary>Closed event with one expense: An (owner-rep) owes 200k, Bình advanced (paid 500k total).</summary>
    private async Task<(string Evt, string An, string Binh, string ExpenseUuid, string AnShareUuid, string BinhShareUuid)>
        SeedClosedEventWithDebtorAsync(HttpClient client)
    {
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        var expense = await CreateExpenseAsync(client, new
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
        return (evt, an, binh, Uuid(expense), ShareUuidForMember(expense, an), ShareUuidForMember(expense, binh));
    }

    private static async Task<string> CreateShareTokenAsync(HttpClient client, string evt, object? body = null)
    {
        using var response = await client.PostAsJsonAsync($"api/v1/events/{evt}/share", body ?? new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").GetProperty("token").GetString()!;
    }

    private static string StreamPath(string token) => $"api/v1/public/shares/{token}/stream";

    // ---- Factory/client helpers parameterized by factory (heartbeat-override tests use a DIFFERENT
    //      factory than the class-level one, so the seeding client and the SSE client share the SAME
    //      app instance - and therefore the same singleton broadcaster - as the overridden config) ----

    private WebApplicationFactory<Program> WithHeartbeatSeconds(int seconds) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Share:StreamHeartbeatSeconds"] = seconds.ToString()
            })));

    private async Task<HttpClient> CreatePremiumClientAsync(WebApplicationFactory<Program> targetFactory)
    {
        var username = NewUsername();
        using var anonymous = targetFactory.CreateClient();
        using var register = await anonymous.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        using var scope = targetFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Users.Where(user => user.Username == username)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Tier, UserTiers.Premium));

        using var login = await anonymous.PostAsJsonAsync("api/v1/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var envelope = await ReadEnvelopeAsync(login);
        var accessToken = envelope.RootElement.GetProperty("data").GetProperty("accessToken").GetString();

        var client = targetFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Forces a share link's <c>ExpiresAt</c> into the past directly in the DB AND busts its
    /// Redis cache entry (best-effort - mirrors <c>EventShareLinkCache.RemoveAsync</c>'s warn-and-continue),
    /// so the next cache-first <c>LookupAsync</c> is guaranteed to fall through to the DB and see the
    /// forced expiry, instead of returning a still-live cached entry from creation time.</summary>
    private static async Task ForceLinkExpiredAsync(WebApplicationFactory<Program> targetFactory, string token)
    {
        using var scope = targetFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.EventShareLinks.Where(link => link.Token == token)
            .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.ExpiresAt, AppDateTime.Now.AddHours(-1)));

        try
        {
            var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            await redis.GetDatabase().KeyDeleteAsync(EventShareLinkCache.CacheKey(token));
        }
        catch
        {
            // Best-effort, mirroring the production cache's own warn-and-continue Redis failure handling.
        }
    }

    // ---- Pre-stream validation: still plain 404 JSON, never a half-opened stream --------------------

    [SkippableFact]
    public async Task StreamPublic_UnknownToken_Returns404JsonNotAStream()
    {
        Fixture.SkipIfNoDb();
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(StreamPath("no-such-token"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("event-stream", response.Content.Headers.ContentType?.ToString() ?? "");
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ShareLinkNotFoundOrExpired);
    }

    [SkippableFact]
    public async Task StreamPublic_ExpiredToken_Returns404Json()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);
        await ForceLinkExpiredAsync(Factory, token);

        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync(StreamPath(token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ShareLinkNotFoundOrExpired);
    }

    [SkippableFact]
    public async Task StreamPublic_RevokedToken_Returns404Json()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);
        using (var delete = await owner.DeleteAsync($"api/v1/events/{evt}/share"))
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync(StreamPath(token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ShareLinkNotFoundOrExpired);
    }

    // ---- Handshake ------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task StreamPublic_ValidToken_ReturnsEventStreamContentTypeAndConnectedFrame()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));

        Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
        Assert.Contains("text/event-stream", stream.Response.Content.Headers.ContentType!.ToString());

        var frame = await stream.ReadFrameAsync(ShortBound);
        Assert.Equal("connected", frame.EventName);
    }

    // ---- Each settled-mutation route pushes "updated" to an open stream on the SAME event -----------

    [SkippableFact]
    public async Task StreamPublic_MemberSettledMutation_PushesUpdatedFrame()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, an, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        using (var mark = await owner.PutAsJsonAsync($"api/v1/events/{evt}/members/{an}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        var frame = await stream.ReadFrameAsync(ShortBound);
        Assert.Equal("updated", frame.EventName);
    }

    [SkippableFact]
    public async Task StreamPublic_ExpenseSettledMutation_PushesUpdatedFrame()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, _, _, expenseUuid, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        using (var mark = await owner.PutAsJsonAsync($"api/v1/expenses/{expenseUuid}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        var frame = await stream.ReadFrameAsync(ShortBound);
        Assert.Equal("updated", frame.EventName);
    }

    [SkippableFact]
    public async Task StreamPublic_ShareSettledMutation_PushesUpdatedFrame()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, _, _, expenseUuid, anShareUuid, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        using (var mark = await owner.PutAsJsonAsync($"api/v1/expenses/{expenseUuid}/shares/{anShareUuid}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        var frame = await stream.ReadFrameAsync(ShortBound);
        Assert.Equal("updated", frame.EventName);
    }

    [SkippableFact]
    public async Task StreamPublic_TwoSequentialMutations_BothUpdatedFramesArriveInOrderOnTheSameStream()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, an, binh, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        // A stream that only ever reads ONE frame after "connected" would never exercise the loop's
        // second iteration, which is exactly where a task-recreated-every-iteration bug would surface
        // (PeriodicTimer.WaitForNextTickAsync throws if called again while a prior call is still
        // pending). Two mutations, read back-to-back on the SAME open connection, prove the stream
        // survives past its first delivered signal.
        using (var mark1 = await owner.PutAsJsonAsync($"api/v1/events/{evt}/members/{an}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark1.StatusCode);
        Assert.Equal("updated", (await stream.ReadFrameAsync(ShortBound)).EventName);

        using (var mark2 = await owner.PutAsJsonAsync($"api/v1/events/{evt}/members/{binh}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark2.StatusCode);
        Assert.Equal("updated", (await stream.ReadFrameAsync(ShortBound)).EventName);
    }

    [SkippableFact]
    public async Task StreamPublic_MutationOnEventWithoutActiveLink_NoSignalArrives()
    {
        using var owner = await CreatePremiumClientAsync();
        var (linkedEvt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, linkedEvt);

        // A second closed event for the SAME owner with NO share link at all.
        var (unlinkedEvt, an, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);

        using var anonymous = Factory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        using (var mark = await owner.PutAsJsonAsync($"api/v1/events/{unlinkedEvt}/members/{an}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        // Negative assertion: nothing arrives on the LINKED event's stream for a mutation on the UNLINKED one.
        await stream.AssertSilentAsync(NegativeBound);
    }

    // ---- Owner revoke / regenerate --------------------------------------------------------------------

    [SkippableFact]
    public async Task StreamPublic_OwnerRevoke_PushesRevokedFrameAndEndsStream()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        using (var revoke = await owner.DeleteAsync($"api/v1/events/{evt}/share"))
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var frame = await stream.ReadFrameAsync(ShortBound);
        Assert.Equal("revoked", frame.EventName);

        // Terminal - the stream ends right after; a further read hits EOF, never another frame.
        await Assert.ThrowsAsync<IOException>(() => stream.ReadFrameAsync(ShortBound));
    }

    [SkippableFact]
    public async Task StreamPublic_OwnerRegenerate_OldTokenGetsRevokedAndNewTokenStillWorks()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var oldToken = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var oldStream = await SseTestClient.ConnectAsync(anonymous, StreamPath(oldToken));
        Assert.Equal("connected", (await oldStream.ReadFrameAsync(ShortBound)).EventName);

        var newToken = await CreateShareTokenAsync(owner, evt, new { regenerate = true });
        Assert.NotEqual(oldToken, newToken);

        var oldFrame = await oldStream.ReadFrameAsync(ShortBound);
        Assert.Equal("revoked", oldFrame.EventName);

        await using var newStream = await SseTestClient.ConnectAsync(anonymous, StreamPath(newToken));
        Assert.Equal(HttpStatusCode.OK, newStream.Response.StatusCode);
        Assert.Equal("connected", (await newStream.ReadFrameAsync(ShortBound)).EventName);
    }

    // ---- Fan-out: two concurrent subscribers on the same token -----------------------------------------

    [SkippableFact]
    public async Task StreamPublic_TwoConcurrentSubscribersSameToken_BothReceiveUpdatedSignal()
    {
        using var owner = await CreatePremiumClientAsync();
        var (evt, an, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        await using var first = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        await using var second = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await first.ReadFrameAsync(ShortBound)).EventName);
        Assert.Equal("connected", (await second.ReadFrameAsync(ShortBound)).EventName);

        using (var mark = await owner.PutAsJsonAsync($"api/v1/events/{evt}/members/{an}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        var firstFrame = await first.ReadFrameAsync(ShortBound);
        var secondFrame = await second.ReadFrameAsync(ShortBound);
        Assert.Equal("updated", firstFrame.EventName);
        Assert.Equal("updated", secondFrame.EventName);
    }

    // ---- Heartbeat (overridden small via WithWebHostBuilder) -------------------------------------------

    [SkippableFact]
    public async Task StreamPublic_IdleStream_EmitsKeepAliveCommentWithinHeartbeatBound()
    {
        using var heartbeatFactory = WithHeartbeatSeconds(1);
        using var owner = await CreatePremiumClientAsync(heartbeatFactory);
        var (evt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = heartbeatFactory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        // No mutation, no revoke - with heartbeat=1s, an idle stream must still see a keep-alive comment.
        var comment = await stream.ReadCommentAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("keep-alive", comment);
    }

    [SkippableFact]
    public async Task StreamPublic_LinkExpiresNaturally_PushesExpiredFrameOnHeartbeatRecheck()
    {
        using var heartbeatFactory = WithHeartbeatSeconds(1);
        using var owner = await CreatePremiumClientAsync(heartbeatFactory);
        var (evt, _, _, _, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = heartbeatFactory.CreateClient();
        await using var stream = await SseTestClient.ConnectAsync(anonymous, StreamPath(token));
        Assert.Equal("connected", (await stream.ReadFrameAsync(ShortBound)).EventName);

        // Nobody explicitly revokes - the link just ages out. Force it into the past directly in the
        // test DB (and bust the Redis cache) so the NEXT heartbeat tick's own re-check notices on its own.
        await ForceLinkExpiredAsync(heartbeatFactory, token);

        var frame = await stream.ReadFrameAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("expired", frame.EventName);

        await Assert.ThrowsAsync<IOException>(() => stream.ReadFrameAsync(ShortBound)); // terminal
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
