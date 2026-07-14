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
/// End-to-end HTTP tests for the wallet CRUD at the kebab-case route <c>api/v1/bank-accounts</c> via
/// WebApplicationFactory (real MariaDB/Redis - skippable). Proves the ApiResult envelope shape, the
/// single-default invariant over HTTP (first account auto-default; atomic set-default swap;
/// delete-of-default promotes another), resource-owned 404 (code 12000, never 403) on
/// get/update/delete/set-default, validation (400 with camelCase <c>error.fields</c>) and the auth guard
/// (401). Assertions target stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class BankAccountsEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private const string Route = "api/v1/bank-accounts";

    private static object ValidBody(
        string bankBin = "970436", string bankName = "Vietcombank",
        string accountNumber = "0123456789", string accountHolderName = "Nguyen Van A") =>
        new { bankBin, bankName, accountNumber, accountHolderName };

    private static async Task<JsonElement> CreateAccountAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync(Route, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").Clone();
    }

    // ---- CRUD + envelope --------------------------------------------------------------------------

    [SkippableFact]
    public async Task Create_FirstAccount_Returns200AutoDefaultWrapped()
    {
        using var client = await CreatePremiumClientAsync();

        using var response = await client.PostAsJsonAsync(Route, ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var root = envelope.RootElement;
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal("970436", data.GetProperty("bankBin").GetString());
        Assert.Equal("Vietcombank", data.GetProperty("bankName").GetString());
        Assert.True(data.GetProperty("isDefault").GetBoolean()); // first account auto-default
        Assert.False(string.IsNullOrEmpty(data.GetProperty("uuid").GetString()));
    }

    [SkippableFact]
    public async Task List_KebabRoute_ReturnsOwnedAccountsDefaultFirst()
    {
        using var client = await CreatePremiumClientAsync();
        await CreateAccountAsync(client, ValidBody(bankName: "Vietcombank"));
        await CreateAccountAsync(client, ValidBody(bankName: "Techcombank"));

        using var response = await client.GetAsync(Route); // the kebab-case route resolves
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var envelope = await ReadEnvelopeAsync(response);
        var accounts = envelope.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, accounts.Count);
        Assert.True(accounts[0].GetProperty("isDefault").GetBoolean()); // default first
    }

    [SkippableFact]
    public async Task Get_OwnedAccount_Returns200()
    {
        using var client = await CreatePremiumClientAsync();
        var created = await CreateAccountAsync(client, ValidBody());
        var uuid = created.GetProperty("uuid").GetString();

        using var response = await client.GetAsync($"{Route}/{uuid}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        Assert.Equal(uuid, envelope.RootElement.GetProperty("data").GetProperty("uuid").GetString());
    }

    [SkippableFact]
    public async Task Update_OwnedAccount_PersistsFieldsKeepsDefault()
    {
        using var client = await CreatePremiumClientAsync();
        var created = await CreateAccountAsync(client, ValidBody());
        var uuid = created.GetProperty("uuid").GetString();

        using var response = await client.PutAsJsonAsync($"{Route}/{uuid}", ValidBody(bankBin: "970422", bankName: "MB Bank", accountNumber: "9998887776", accountHolderName: "Tran Thi B"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("MB Bank", data.GetProperty("bankName").GetString());
        Assert.Equal("970422", data.GetProperty("bankBin").GetString());
        Assert.True(data.GetProperty("isDefault").GetBoolean()); // unchanged by update
    }

    [SkippableFact]
    public async Task SetDefault_SwapsAtomicallyOverHttp()
    {
        using var client = await CreatePremiumClientAsync();
        var first = await CreateAccountAsync(client, ValidBody(bankName: "Vietcombank"));  // default
        var second = await CreateAccountAsync(client, ValidBody(bankName: "Techcombank"));
        var secondUuid = second.GetProperty("uuid").GetString();

        using var setResponse = await client.PutAsync($"{Route}/{secondUuid}/default", null);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        using var listResponse = await client.GetAsync(Route);
        using var envelope = await ReadEnvelopeAsync(listResponse);
        var accounts = envelope.RootElement.GetProperty("data").EnumerateArray().ToList();

        // Exactly one default, and it is the second account.
        Assert.Single(accounts, account => account.GetProperty("isDefault").GetBoolean());
        Assert.True(accounts.Single(account => account.GetProperty("uuid").GetString() == secondUuid).GetProperty("isDefault").GetBoolean());
        Assert.False(accounts.Single(account => account.GetProperty("uuid").GetString() == first.GetProperty("uuid").GetString()).GetProperty("isDefault").GetBoolean());
    }

    [SkippableFact]
    public async Task Delete_Default_PromotesAnotherOverHttp()
    {
        using var client = await CreatePremiumClientAsync();
        var first = await CreateAccountAsync(client, ValidBody(bankName: "Vietcombank"));  // default
        await CreateAccountAsync(client, ValidBody(bankName: "Techcombank"));
        var firstUuid = first.GetProperty("uuid").GetString();

        using var deleteResponse = await client.DeleteAsync($"{Route}/{firstUuid}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var listResponse = await client.GetAsync(Route);
        using var envelope = await ReadEnvelopeAsync(listResponse);
        var accounts = envelope.RootElement.GetProperty("data").EnumerateArray().ToList();

        Assert.Single(accounts); // one remains
        Assert.True(accounts[0].GetProperty("isDefault").GetBoolean()); // promoted to default
    }

    [SkippableFact]
    public async Task Delete_LastAccount_LeavesEmptyWallet()
    {
        using var client = await CreatePremiumClientAsync();
        var only = await CreateAccountAsync(client, ValidBody());

        using var deleteResponse = await client.DeleteAsync($"{Route}/{only.GetProperty("uuid").GetString()}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var listResponse = await client.GetAsync(Route);
        using var envelope = await ReadEnvelopeAsync(listResponse);
        Assert.Empty(envelope.RootElement.GetProperty("data").EnumerateArray());
    }

    // ---- Resource-owned (404, never 403) ----------------------------------------------------------

    [SkippableFact]
    public async Task Get_AnotherUsersAccount_Returns404Code12000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        var account = await CreateAccountAsync(owner, ValidBody());
        var uuid = account.GetProperty("uuid").GetString();

        using var response = await stranger.GetAsync($"{Route}/{uuid}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // never 403
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankAccountNotFound);
    }

    [SkippableFact]
    public async Task Update_AnotherUsersAccount_Returns404Code12000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        var account = await CreateAccountAsync(owner, ValidBody());
        var uuid = account.GetProperty("uuid").GetString();

        using var response = await stranger.PutAsJsonAsync($"{Route}/{uuid}", ValidBody(bankName: "Hacked"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankAccountNotFound);
    }

    [SkippableFact]
    public async Task SetDefault_AnotherUsersAccount_Returns404Code12000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        var account = await CreateAccountAsync(owner, ValidBody());
        var uuid = account.GetProperty("uuid").GetString();

        using var response = await stranger.PutAsync($"{Route}/{uuid}/default", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankAccountNotFound);
    }

    [SkippableFact]
    public async Task Delete_AnotherUsersAccount_Returns404Code12000()
    {
        using var owner = await CreatePremiumClientAsync();
        using var stranger = await CreatePremiumClientAsync();
        var account = await CreateAccountAsync(owner, ValidBody());
        var uuid = account.GetProperty("uuid").GetString();

        using var response = await stranger.DeleteAsync($"{Route}/{uuid}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankAccountNotFound);
    }

    [SkippableFact]
    public async Task Get_UnknownUuid_Returns404Code12000()
    {
        using var client = await CreatePremiumClientAsync();

        using var response = await client.GetAsync($"{Route}/no-such-uuid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.BankAccountNotFound);
    }

    // ---- Validation -------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Create_InvalidBankBin_Returns400WithCamelCaseFields()
    {
        using var client = await CreatePremiumClientAsync();

        using var response = await client.PostAsJsonAsync(Route, ValidBody(bankBin: "12"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        var fields = envelope.RootElement.GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("bankBin", out var binErrors)); // camelCase field key
        Assert.False(string.IsNullOrWhiteSpace(binErrors[0].GetString())); // Vietnamese message present
    }

    [SkippableFact]
    public async Task Create_InvalidAccountNumber_Returns400WithCamelCaseFields()
    {
        using var client = await CreatePremiumClientAsync();

        using var response = await client.PostAsJsonAsync(Route, ValidBody(accountNumber: "abc"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("accountNumber", out _));
    }

    // ---- Auth -------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task List_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertErrorEnvelope(await ReadEnvelopeAsync(response), ErrorCodes.Unauthorized);
    }

    [SkippableFact]
    public async Task Create_Anonymous_Returns401()
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, ValidBody());

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
            await context.BankAccounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
