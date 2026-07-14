using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the guarded share sub-routes
/// (<c>POST|PUT|DELETE api/v1/expenses/{uuid}/shares[/{shareUuid}]</c>) via WebApplicationFactory
/// (real MariaDB/Redis - skippable). Covers add / edit (incl. change-member) / delete, the owner-rep
/// share delete refusal (7002), the owner-rep member-change refusal (7002), the duplicate-member
/// (7003) and invalid-member (7001) guards, and the resource-owned 404 (code 7000/6000, never 403).
/// Assertions target stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class ExpenseSharesEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static JsonElement ShareForMember(JsonElement expense, string memberUuid) =>
        expense.GetProperty("shares").EnumerateArray()
            .Single(share => share.GetProperty("member").GetProperty("uuid").GetString() == memberUuid);

    private static JsonElement OwnerRepShare(JsonElement expense) =>
        expense.GetProperty("shares").EnumerateArray()
            .Single(share => share.GetProperty("member").GetProperty("isOwnerRepresentative").GetBoolean());

    [SkippableFact]
    public async Task AddShare_ValidMember_AppearsOnTheExpense()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });

        using var response = await client.PostAsJsonAsync($"api/v1/expenses/{Uuid(created)}/shares", new { memberUuid = an, amount = 25_000m, note = "Nợ" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expense = await GetExpenseAsync(client, Uuid(created));
        var anShare = ShareForMember(expense, an);
        Assert.Equal(25_000m, anShare.GetProperty("amount").GetDecimal());
    }

    [SkippableFact]
    public async Task AddShare_DuplicateMember_Returns400Code7003()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRep = await OwnerRepUuidAsync(client);
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon }); // owner-rep 0đ auto-injected

        using var response = await client.PostAsJsonAsync($"api/v1/expenses/{Uuid(created)}/shares", new { memberUuid = ownerRep, amount = 10_000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.DuplicateShareMember);
    }

    [SkippableFact]
    public async Task AddShare_InvalidMember_Returns400Code7001()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });

        using var response = await client.PostAsJsonAsync($"api/v1/expenses/{Uuid(created)}/shares",
            new { memberUuid = "no-such-member", amount = 10_000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ShareMemberInvalid);
    }

    [SkippableFact]
    public async Task UpdateShare_ChangeMemberAndAmount_Persists()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var binh = await CreateMemberAsync(client, "Bình");
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = 25_000m } }
        });
        var anShareUuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(created)), an));

        using var response = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(created)}/shares/{anShareUuid}",
            new { memberUuid = binh, amount = 30_000m }); // change member + amount
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var binhShare = ShareForMember(await GetExpenseAsync(client, Uuid(created)), binh);
        Assert.Equal(30_000m, binhShare.GetProperty("amount").GetDecimal());
    }

    [SkippableFact]
    public async Task UpdateShare_ChangeOwnerRepMemberAway_Returns400Code7002()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon }); // owner-rep share auto-injected
        var ownerRepShareUuid = Uuid(OwnerRepShare(await GetExpenseAsync(client, Uuid(created))));

        using var response = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(created)}/shares/{ownerRepShareUuid}",
            new { memberUuid = an, amount = 10_000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.OwnerRepresentativeShareNotDeletable);
    }

    [SkippableFact]
    public async Task DeleteShare_Regular_RemovesIt()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = 25_000m } }
        });
        var anShareUuid = Uuid(ShareForMember(await GetExpenseAsync(client, Uuid(created)), an));

        using var response = await client.DeleteAsync($"api/v1/expenses/{Uuid(created)}/shares/{anShareUuid}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expense = await GetExpenseAsync(client, Uuid(created));
        Assert.DoesNotContain(expense.GetProperty("shares").EnumerateArray(),
            share => share.GetProperty("member").GetProperty("uuid").GetString() == an);
    }

    [SkippableFact]
    public async Task DeleteShare_OwnerRepShare_Returns400Code7002()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });
        var ownerRepShareUuid = Uuid(OwnerRepShare(await GetExpenseAsync(client, Uuid(created))));

        using var response = await client.DeleteAsync($"api/v1/expenses/{Uuid(created)}/shares/{ownerRepShareUuid}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.OwnerRepresentativeShareNotDeletable);
    }

    [SkippableFact]
    public async Task DeleteShare_UnknownShare_Returns404Code7000()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });

        using var response = await client.DeleteAsync($"api/v1/expenses/{Uuid(created)}/shares/no-such-share");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ShareNotFound);
    }

    [SkippableFact]
    public async Task AnotherUsersExpenseShareRoutes_Return404_Never403()
    {
        using var ownerClient = await CreateAuthorizedClientAsync();
        using var strangerClient = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(ownerClient, "An");
        var created = await CreateExpenseAsync(ownerClient, new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = 25_000m } }
        });
        var uuid = Uuid(created);
        var anShareUuid = Uuid(ShareForMember(await GetExpenseAsync(ownerClient, uuid), an));
        var strangerMember = await CreateMemberAsync(strangerClient, "Ngoài");

        // Add is scoped by the expense -> 6000; update/delete by the share -> 7000.
        using var addResponse = await strangerClient.PostAsJsonAsync($"api/v1/expenses/{uuid}/shares", new { memberUuid = strangerMember, amount = 10_000m });
        using var updateResponse = await strangerClient.PutAsJsonAsync($"api/v1/expenses/{uuid}/shares/{anShareUuid}", new { memberUuid = strangerMember, amount = 10_000m });
        using var deleteResponse = await strangerClient.DeleteAsync($"api/v1/expenses/{uuid}/shares/{anShareUuid}");

        Assert.Equal(HttpStatusCode.NotFound, addResponse.StatusCode);
        using (var envelope = await ReadEnvelopeAsync(addResponse))
            AssertErrorEnvelope(envelope, ErrorCodes.ExpenseNotFound);

        foreach (var response in new[] { updateResponse, deleteResponse })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var envelope = await ReadEnvelopeAsync(response);
            AssertErrorEnvelope(envelope, ErrorCodes.ShareNotFound);
        }
    }

    [SkippableFact]
    public async Task AddShare_NegativeAmount_Returns400Code1001()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });

        using var response = await client.PostAsJsonAsync($"api/v1/expenses/{Uuid(created)}/shares", new { memberUuid = an, amount = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
    }
}
