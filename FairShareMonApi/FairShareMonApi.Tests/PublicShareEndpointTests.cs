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
/// End-to-end HTTP tests for the anonymous public share routes
/// <c>GET api/v1/public/shares/{token}</c> and <c>.../{token}/qr/members</c> via WebApplicationFactory
/// (real MariaDB/Redis - skippable). All GETs use a client with NO auth header, proving the
/// <c>[AllowAnonymous]</c> route works and that <c>AuthenticatedUser</c> is never read. Asserts the
/// wrapped <c>PublicEventShareResponse</c> (event name, per-member rows, per-expense breakdown, counts,
/// <c>hasQr</c>), the LIVE overlay (marking a member settled while authed is reflected on a re-GET), the
/// per-member VietQR list (PNG data URLs; empty when nobody owes or no snapshot), and that an unknown /
/// revoked token yields 404 <c>ShareLinkNotFoundOrExpired</c> (16000).
/// </summary>
[Collection("AuthIntegration")]
public class PublicShareEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    private static async Task CreateBankAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Closed event with one expense: An (owner-rep) owes 200k, Bình advanced (paid 500k total).</summary>
    private async Task<(string Evt, string An, string Binh)> SeedClosedEventWithDebtorAsync(HttpClient client)
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
        return (evt, an, binh);
    }

    /// <summary>Closed event where nobody owes: Bình pays and holds the only non-zero share.</summary>
    private async Task<string> SeedClosedEventNoDebtorAsync(HttpClient client)
    {
        var binh = await CreateMemberAsync(client, "Bình");
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16);
        await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            eventUuid = evt,
            shares = new[] { new { memberUuid = binh, amount = 500_000m } }
        });
        await CloseEventAsync(client, evt);
        return evt;
    }

    private static async Task<string> CreateShareTokenAsync(HttpClient client, string evt, object? body = null)
    {
        using var response = await client.PostAsJsonAsync($"api/v1/events/{evt}/share", body ?? new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").GetProperty("token").GetString()!;
    }

    private static bool StartsWithPngMagic(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == PngMagic[0] && bytes[1] == PngMagic[1] && bytes[2] == PngMagic[2] && bytes[3] == PngMagic[3];

    // ---- GET public report ------------------------------------------------------------------------

    [SkippableFact]
    public async Task GetPublic_NoAuthHeader_Returns200WithReportAndPerExpenseBreakdown()
    {
        using var owner = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(owner);
        var (evt, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        // Anonymous client - NO Authorization header.
        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"api/v1/public/shares/{token}");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode); // proves the anonymous route
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("application/json", response.Content.Headers.ContentType!.ToString());

        using var envelope = await ReadEnvelopeAsync(response);
        var root = envelope.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal("Đà Lạt", data.GetProperty("eventName").GetString());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("closedAt").ValueKind);
        Assert.True(data.GetProperty("hasQr").GetBoolean());
        Assert.Equal(200_000m, data.GetProperty("totalOutstanding").GetDecimal());
        Assert.Equal(1, data.GetProperty("owingMemberCount").GetInt32());

        // Per-member rows + per-expense breakdown with per-share detail.
        Assert.Equal(2, data.GetProperty("rows").GetArrayLength());
        var expense = Assert.Single(data.GetProperty("expenses").EnumerateArray().ToList());
        Assert.Equal("Ăn tối", expense.GetProperty("name").GetString());
        Assert.Equal("Bình", expense.GetProperty("payerName").GetString());
        Assert.Equal(2, expense.GetProperty("shares").GetArrayLength());
        foreach (var share in expense.GetProperty("shares").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(share.GetProperty("memberUuid").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(share.GetProperty("memberName").GetString()));
            Assert.True(share.TryGetProperty("isSettled", out _));
        }
    }

    [SkippableFact]
    public async Task GetPublic_NoWalletAccount_HasQrFalse()
    {
        using var owner = await CreatePremiumClientAsync(); // no bank account
        var (evt, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"api/v1/public/shares/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.False(envelope.RootElement.GetProperty("data").GetProperty("hasQr").GetBoolean());
    }

    [SkippableFact]
    public async Task GetPublic_UnknownToken_Returns404Code16000()
    {
        Fixture.SkipIfNoDb();
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync("api/v1/public/shares/no-such-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ShareLinkNotFoundOrExpired);
    }

    [SkippableFact]
    public async Task GetPublic_RevokedToken_Returns404Code16000()
    {
        using var owner = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(owner);
        var (evt, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);
        using (var delete = await owner.DeleteAsync($"api/v1/events/{evt}/share"))
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"api/v1/public/shares/{token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ShareLinkNotFoundOrExpired);
    }

    [SkippableFact]
    public async Task GetPublic_LiveOverlay_ReflectsSettledChangeOnReGet()
    {
        using var owner = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(owner);
        var (evt, an, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);
        using var anonymous = Factory.CreateClient();

        // Before: An still owes 200k.
        using (var before = await anonymous.GetAsync($"api/v1/public/shares/{token}"))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            using var envelope = await ReadEnvelopeAsync(before);
            var data = envelope.RootElement.GetProperty("data");
            Assert.Equal(200_000m, data.GetProperty("totalOutstanding").GetDecimal());
            Assert.Equal(200_000m, RowFor(data, an).GetProperty("outstanding").GetDecimal());
            Assert.False(RowFor(data, an).GetProperty("isSettled").GetBoolean());
        }

        // Owner marks An settled (authed Layer B toggle).
        using (var mark = await owner.PutAsJsonAsync($"api/v1/events/{evt}/members/{an}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        // After: the LIVE public read reflects the changed overlay (Decision 3).
        using (var after = await anonymous.GetAsync($"api/v1/public/shares/{token}"))
        {
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
            using var envelope = await ReadEnvelopeAsync(after);
            var data = envelope.RootElement.GetProperty("data");
            Assert.Equal(0m, data.GetProperty("totalOutstanding").GetDecimal());
            Assert.Equal(0, data.GetProperty("owingMemberCount").GetInt32());
            Assert.Equal(0m, RowFor(data, an).GetProperty("outstanding").GetDecimal());
            Assert.True(RowFor(data, an).GetProperty("isSettled").GetBoolean());
        }
    }

    // ---- GET public per-member QR list ------------------------------------------------------------

    [SkippableFact]
    public async Task GetPublicMemberQrs_NoAuthHeader_Returns200PngDataUrls()
    {
        using var owner = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(owner);
        var (evt, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"api/v1/public/shares/{token}/qr/members");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        var only = Assert.Single(data.EnumerateArray().ToList()); // only An owes
        Assert.Equal(200_000m, only.GetProperty("amount").GetDecimal());
        const string prefix = "data:image/png;base64,";
        var image = only.GetProperty("image").GetString()!;
        Assert.StartsWith(prefix, image);
        Assert.True(StartsWithPngMagic(Convert.FromBase64String(image[prefix.Length..])));
    }

    [SkippableFact]
    public async Task GetPublicMemberQrs_NobodyOwes_ReturnsEmptyList()
    {
        using var owner = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(owner);
        var evt = await SeedClosedEventNoDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"api/v1/public/shares/{token}/qr/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // share path softens 12003 -> empty list
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.Empty(envelope.RootElement.GetProperty("data").EnumerateArray().ToList());
    }

    [SkippableFact]
    public async Task GetPublicMemberQrs_NoSnapshot_ReturnsEmptyList()
    {
        using var owner = await CreatePremiumClientAsync(); // no bank account -> hasQr false
        var (evt, _, _) = await SeedClosedEventWithDebtorAsync(owner);
        var token = await CreateShareTokenAsync(owner, evt);

        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"api/v1/public/shares/{token}/qr/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.Empty(envelope.RootElement.GetProperty("data").EnumerateArray().ToList());
    }

    [SkippableFact]
    public async Task GetPublicMemberQrs_UnknownToken_Returns404Code16000()
    {
        Fixture.SkipIfNoDb();
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync("api/v1/public/shares/no-such-token/qr/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ShareLinkNotFoundOrExpired);
    }

    private static JsonElement RowFor(JsonElement data, string memberUuid) =>
        data.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("memberUuid").GetString() == memberUuid);

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
