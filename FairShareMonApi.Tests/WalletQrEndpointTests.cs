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
/// End-to-end HTTP tests for the QR routes <c>GET api/v1/expenses/{uuid}/qr</c> and
/// <c>GET api/v1/events/{uuid}/qr</c> via WebApplicationFactory (real MariaDB/Redis - skippable).
/// Proves the M8 file-response bypass: the success path streams RAW PNG bytes (magic 89 50 4E 47), NOT
/// an ApiResult envelope; <c>?format=payload</c> returns a wrapped VietQR string whose CRC re-validates
/// (independent check) and whose amount equals the expense total; the event route returns the composite
/// PNG (attachment) for a closed event with debtors; and the error/edge paths return the wrapped
/// envelope with the right codes (12001 no account, 12002 open event, 12003 nobody owes, 6000/9000
/// resource-owned, 401 anonymous). Note: account resolution precedes the resource check, so foreign-
/// resource tests give the requester their own account (matching the shipped behaviour).
/// </summary>
[Collection("AuthIntegration")]
public class WalletQrEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day15Noon = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    private static bool StartsWithPngMagic(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == PngMagic[0] && bytes[1] == PngMagic[1] && bytes[2] == PngMagic[2] && bytes[3] == PngMagic[3];

    private static bool ParsesAsApiResultEnvelope(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("isSuccess", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task CreateBankAccountAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> CreateSimpleExpenseUuidAsync(HttpClient client, decimal total = 500_000m)
    {
        var an = await OwnerRepUuidAsync(client);
        var binh = await CreateMemberAsync(client, "Bình");
        var data = await CreateExpenseAsync(client, new
        {
            name = "Ăn tối",
            expenseTime = Day15Noon,
            payerMemberUuid = binh,
            shares = new[]
            {
                new { memberUuid = an, amount = 0m },
                new { memberUuid = binh, amount = total }
            }
        });
        return Uuid(data);
    }

    /// <summary>Seeds a closed event with one still-owing member (An owes 200k, Bình advanced).</summary>
    private async Task<string> CreateClosedEventWithDebtorAsync(HttpClient client)
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

    // ---- Expense QR: success (the bypass) ---------------------------------------------------------

    [SkippableFact]
    public async Task ExpenseQr_Default_Returns200RawPngNotWrapped()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var expense = await CreateSimpleExpenseUuidAsync(client);

        using var response = await client.GetAsync($"api/v1/expenses/{expense}/qr");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.ToString());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(StartsWithPngMagic(bytes));                // PNG magic
        Assert.False(ParsesAsApiResultEnvelope(bytes));        // raw image, not the ApiResult wrapper
    }

    [SkippableFact]
    public async Task ExpenseQr_FormatPayload_Returns200JsonVietQrStringWithValidCrcAndAmount()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var expense = await CreateSimpleExpenseUuidAsync(client, total: 500_000m);

        using var response = await client.GetAsync($"api/v1/expenses/{expense}/qr?format=payload");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("json", response.Content.Headers.ContentType!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var envelope = await ReadEnvelopeAsync(response);
        Assert.True(envelope.RootElement.GetProperty("isSuccess").GetBoolean());
        var payload = envelope.RootElement.GetProperty("data").GetString()!;

        // CRC re-validates with an independent implementation.
        Assert.Equal(IndependentCrc16CcittFalse(payload[..^4]).ToString("X4"), payload[^4..]);
        // Amount (field 54) equals the expense total.
        Assert.Equal("500000", ParseTlv(payload)["54"]);
    }

    // ---- Expense QR: error / edge (wrapped) -------------------------------------------------------

    [SkippableFact]
    public async Task ExpenseQr_NoBankAccount_Returns400Code12001()
    {
        using var client = await CreatePremiumClientAsync(); // no wallet configured
        var expense = await CreateSimpleExpenseUuidAsync(client);

        using var response = await client.GetAsync($"api/v1/expenses/{expense}/qr");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.NoBankAccountForQr);
    }

    [SkippableFact]
    public async Task ExpenseQr_AnotherUsersExpense_Returns404Code6000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(stranger); // requester has an account (resolution precedes resource check)
        var expense = await CreateSimpleExpenseUuidAsync(owner);

        using var response = await stranger.GetAsync($"api/v1/expenses/{expense}/qr");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.ExpenseNotFound);
    }

    [SkippableFact]
    public async Task ExpenseQr_OverrideAnotherUsersAccount_Returns404Code12000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(stranger);
        // Owner has an account we will try to target from the stranger's session.
        using var create = await owner.PostAsJsonAsync("api/v1/bank-accounts",
            new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Owner" });
        using var createEnvelope = await ReadEnvelopeAsync(create);
        var ownerAccountUuid = createEnvelope.RootElement.GetProperty("data").GetProperty("uuid").GetString();
        var expense = await CreateSimpleExpenseUuidAsync(stranger);

        using var response = await stranger.GetAsync($"api/v1/expenses/{expense}/qr?bankAccountUuid={ownerAccountUuid}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankAccountNotFound);
    }

    [SkippableFact]
    public async Task ExpenseQr_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("api/v1/expenses/some-uuid/qr");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    // ---- Event QR: success ------------------------------------------------------------------------

    [SkippableFact]
    public async Task EventQr_ClosedWithDebtor_Returns200CompositePngAttachment()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await CreateClosedEventWithDebtorAsync(client);

        using var response = await client.GetAsync($"api/v1/events/{evt}/qr");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(StartsWithPngMagic(bytes));
        Assert.False(ParsesAsApiResultEnvelope(bytes));
        Assert.True(bytes.Length > 1000, "a composite QR PNG should be non-trivial in size");
    }

    // ---- Event QR: error / edge -------------------------------------------------------------------

    [SkippableFact]
    public async Task EventQr_OpenEvent_Returns400Code12002()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16); // not closed

        using var response = await client.GetAsync($"api/v1/events/{evt}/qr");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotClosedForQr);
    }

    [SkippableFact]
    public async Task EventQr_ClosedButNobodyOwes_Returns400Code12003()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(client);
        var evt = await CreateEventUuidAsync(client, "Đà Lạt", Day14, Day16); // no expenses -> nobody owes
        await CloseEventAsync(client, evt);

        using var response = await client.GetAsync($"api/v1/events/{evt}/qr");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.NoOutstandingDebtForQr);
    }

    [SkippableFact]
    public async Task EventQr_NoBankAccount_Returns400Code12001()
    {
        using var client = await CreatePremiumClientAsync(); // no wallet
        var evt = await CreateClosedEventWithDebtorAsync(client);

        using var response = await client.GetAsync($"api/v1/events/{evt}/qr");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.NoBankAccountForQr);
    }

    [SkippableFact]
    public async Task EventQr_AnotherUsersEvent_Returns404Code9000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        await CreateBankAccountAsync(stranger); // requester has an account
        var evt = await CreateClosedEventWithDebtorAsync(owner);

        using var response = await stranger.GetAsync($"api/v1/events/{evt}/qr");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.EventNotFound);
    }

    [SkippableFact]
    public async Task EventQr_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("api/v1/events/some-uuid/qr");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    // ---- Test-local helpers -----------------------------------------------------------------------

    private static Dictionary<string, string> ParseTlv(string data)
    {
        var map = new Dictionary<string, string>();
        var i = 0;
        while (i < data.Length)
        {
            var id = data.Substring(i, 2);
            var length = int.Parse(data.Substring(i + 2, 2));
            map[id] = data.Substring(i + 4, length);
            i += 4 + length;
        }

        return map;
    }

    private static ushort IndependentCrc16CcittFalse(string data)
    {
        var table = new ushort[256];
        for (var n = 0; n < 256; n++)
        {
            var entry = (ushort)(n << 8);
            for (var bit = 0; bit < 8; bit++)
                entry = (ushort)((entry & 0x8000) != 0 ? (entry << 1) ^ 0x1021 : entry << 1);
            table[n] = entry;
        }

        ushort crc = 0xFFFF;
        foreach (var ch in data)
            crc = (ushort)((crc << 8) ^ table[((crc >> 8) ^ (byte)ch) & 0xFF]);

        return crc;
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
            await context.BankAccounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
