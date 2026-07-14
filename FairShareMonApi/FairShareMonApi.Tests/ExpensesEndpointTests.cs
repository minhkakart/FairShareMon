using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP tests for the guarded expense endpoints (list/get/create/update/delete/settled)
/// via WebApplicationFactory (real MariaDB/Redis - skippable). Covers create-with-shares + defaults +
/// owner-rep 0đ auto-inject, the full GET DTO (total/category/payer/tags/shares), the summary list +
/// AND filters, update general info + tag replace, the settled toggle (amounts unchanged), delete +
/// subsequent 404, §4.2/§4.8 rejection of a deleted category (6002), the resource-owned 404
/// (code 6000, never 403), validation (<c>error.fields</c>, camelCase) incl. a negative amount, and
/// the auth guard. Assertions target stable error CODES.
/// </summary>
[Collection("AuthIntegration")]
public class ExpensesEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : ExpenseApiTestBase(factory, fixture)
{
    private static async Task<JsonElement[]> ListAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"api/v1/expenses{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        return envelope.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    [SkippableFact]
    public async Task CreateExpense_WithThreeShares_GetReturnsFullDtoWithTotalCategoryPayerTagsAndShares()
    {
        using var client = await CreateAuthorizedClientAsync();
        var ownerRep = await OwnerRepUuidAsync(client);
        var an = await CreateMemberAsync(client, "An");
        var binh = await CreateMemberAsync(client, "Bình");
        var tag = await CreateTagAsync(client, "Công tác");

        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            description = "Cơm văn phòng",
            expenseTime = Noon,
            tagUuids = new[] { tag },
            shares = new[]
            {
                new { memberUuid = ownerRep, amount = 60_000m, note = (string?)null },
                new { memberUuid = an, amount = 30_000m, note = "Nợ" },
                new { memberUuid = binh, amount = 10_000m, note = (string?)null }
            }
        });

        var expense = await GetExpenseAsync(client, Uuid(created));
        Assert.Equal("Ăn trưa", expense.GetProperty("name").GetString());
        Assert.Equal(100_000m, expense.GetProperty("total").GetDecimal()); // derived total
        Assert.Equal(3, expense.GetProperty("shares").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(expense.GetProperty("category").GetProperty("name").GetString()));
        Assert.True(expense.GetProperty("payer").GetProperty("isOwnerRepresentative").GetBoolean()); // default payer
        Assert.Equal("Công tác", expense.GetProperty("tags")[0].GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task CreateExpense_OmittedPayerCategoryAndOwnerRepShare_AppliesDefaultsAndInjectsZeroShare()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var defaultCategory = await DefaultCategoryAsync(client);

        var created = await CreateExpenseAsync(client, new
        {
            name = "Cà phê",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = 50_000m } } // owner-rep omitted
        });

        var expense = await GetExpenseAsync(client, Uuid(created));
        Assert.True(expense.GetProperty("payer").GetProperty("isOwnerRepresentative").GetBoolean()); // default payer
        Assert.Equal(Uuid(defaultCategory), Uuid(expense.GetProperty("category"))); // default category
        var shares = expense.GetProperty("shares").EnumerateArray().ToArray();
        Assert.Equal(2, shares.Length); // An + auto-injected owner-rep
        var ownerRepShare = shares.Single(share => share.GetProperty("member").GetProperty("isOwnerRepresentative").GetBoolean());
        Assert.Equal(0m, ownerRepShare.GetProperty("amount").GetDecimal()); // 0đ owner-rep share
    }

    [SkippableFact]
    public async Task CreateExpense_DeletedCategory_Returns400Code6002()
    {
        using var client = await CreateAuthorizedClientAsync();
        // Create then soft-delete a category so it exists but is not selectable (§4.8).
        using var createCat = await client.PostAsJsonAsync("api/v1/categories", new { name = "Tạm", color = "#3B82F6" });
        using var catEnvelope = await ReadEnvelopeAsync(createCat);
        var deletedCategoryUuid = catEnvelope.RootElement.GetProperty("data").GetProperty("uuid").GetString()!;
        await DeleteCategoryAsync(client, deletedCategoryUuid);

        using var response = await client.PostAsJsonAsync("api/v1/expenses", new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            categoryUuid = deletedCategoryUuid
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ExpenseCategoryInvalid);
    }

    [SkippableFact]
    public async Task ListExpenses_ReturnsSummaryDtoWithTotalAndShareCount()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = 40_000m } }
        });

        var list = await ListAsync(client);

        var summary = Assert.Single(list);
        Assert.Equal("Ăn trưa", summary.GetProperty("name").GetString());
        Assert.Equal(40_000m, summary.GetProperty("total").GetDecimal());
        Assert.Equal(2, summary.GetProperty("shareCount").GetInt32()); // An + owner-rep
    }

    [SkippableFact]
    public async Task ListExpenses_SettledFilter_ReturnsOnlyMatching()
    {
        using var client = await CreateAuthorizedClientAsync();
        var settled = await CreateExpenseAsync(client, new { name = "Đã trả", expenseTime = Noon });
        await CreateExpenseAsync(client, new { name = "Chưa trả", expenseTime = Noon });
        using (var toggle = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(settled)}/settled", new { isSettled = true }))
            Assert.Equal(HttpStatusCode.OK, toggle.StatusCode);

        var list = await ListAsync(client, "?settled=true");

        var only = Assert.Single(list);
        Assert.Equal("Đã trả", only.GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task UpdateExpense_GeneralInfoAndTagReplace_Persists()
    {
        using var client = await CreateAuthorizedClientAsync();
        var tagA = await CreateTagAsync(client, "A");
        var tagB = await CreateTagAsync(client, "B");
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon, tagUuids = new[] { tagA } });

        using var response = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(created)}", new
        {
            name = "Ăn tối",
            expenseTime = Noon,
            tagUuids = new[] { tagB } // full replace A -> B
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expense = await GetExpenseAsync(client, Uuid(created));
        Assert.Equal("Ăn tối", expense.GetProperty("name").GetString());
        var tags = expense.GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("name").GetString()).ToArray();
        Assert.Equal("B", Assert.Single(tags)); // full replace A -> B
    }

    [SkippableFact]
    public async Task SetSettled_FlipsFlagWithoutChangingAmounts()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = 40_000m } }
        });
        var totalBefore = (await GetExpenseAsync(client, Uuid(created))).GetProperty("total").GetDecimal();

        using var response = await client.PutAsJsonAsync($"api/v1/expenses/{Uuid(created)}/settled", new { isSettled = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await GetExpenseAsync(client, Uuid(created));
        Assert.True(after.GetProperty("isSettled").GetBoolean());
        Assert.Equal(totalBefore, after.GetProperty("total").GetDecimal()); // amounts unchanged
    }

    [SkippableFact]
    public async Task DeleteExpense_ThenGet_Returns404Code6000()
    {
        using var client = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(client, new { name = "Ăn trưa", expenseTime = Noon });

        using var deleteResponse = await client.DeleteAsync($"api/v1/expenses/{Uuid(created)}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var getResponse = await client.GetAsync($"api/v1/expenses/{Uuid(created)}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        using var envelope = await ReadEnvelopeAsync(getResponse);
        AssertErrorEnvelope(envelope, ErrorCodes.ExpenseNotFound);
    }

    [SkippableFact]
    public async Task AnotherUsersExpense_Returns404Code6000_OnGetPutDeleteSettled_Never403()
    {
        using var ownerClient = await CreateAuthorizedClientAsync();
        using var strangerClient = await CreateAuthorizedClientAsync();
        var created = await CreateExpenseAsync(ownerClient, new { name = "Ăn trưa", expenseTime = Noon });
        var uuid = Uuid(created);

        using var getResponse = await strangerClient.GetAsync($"api/v1/expenses/{uuid}");
        using var putResponse = await strangerClient.PutAsJsonAsync($"api/v1/expenses/{uuid}", new { name = "Hacked", expenseTime = Noon });
        using var deleteResponse = await strangerClient.DeleteAsync($"api/v1/expenses/{uuid}");
        using var settledResponse = await strangerClient.PutAsJsonAsync($"api/v1/expenses/{uuid}/settled", new { isSettled = true });

        foreach (var response in new[] { getResponse, putResponse, deleteResponse, settledResponse })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404, never 403
            using var envelope = await ReadEnvelopeAsync(response);
            AssertErrorEnvelope(envelope, ErrorCodes.ExpenseNotFound);
        }
    }

    [SkippableFact]
    public async Task CreateExpense_EmptyName_Returns400Code1001WithNameField()
    {
        using var client = await CreateAuthorizedClientAsync();

        using var response = await client.PostAsJsonAsync("api/v1/expenses", new { name = "", expenseTime = Noon });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
        Assert.True(envelope.RootElement.GetProperty("error").GetProperty("fields").TryGetProperty("name", out _)); // camelCase key
    }

    [SkippableFact]
    public async Task CreateExpense_NegativeShareAmount_Returns400Code1001()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");

        using var response = await client.PostAsJsonAsync("api/v1/expenses", new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = -1m } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var envelope = await ReadEnvelopeAsync(response);
        AssertErrorEnvelope(envelope, ErrorCodes.ValidationFailed);
    }

    [SkippableFact]
    public async Task GetExpense_MemberSoftDeletedAfterLinking_StillDisplaysOnTheShare()
    {
        using var client = await CreateAuthorizedClientAsync();
        var an = await CreateMemberAsync(client, "An");
        var created = await CreateExpenseAsync(client, new
        {
            name = "Ăn trưa",
            expenseTime = Noon,
            shares = new[] { new { memberUuid = an, amount = 40_000m } }
        });

        await DeleteMemberAsync(client, an); // soft-delete AFTER the link (§4.7)

        var expense = await GetExpenseAsync(client, Uuid(created));
        var anShare = expense.GetProperty("shares").EnumerateArray()
            .Single(share => share.GetProperty("member").GetProperty("uuid").GetString() == an);
        Assert.True(anShare.GetProperty("member").GetProperty("isDeleted").GetBoolean()); // still displays the deleted member
    }

    [SkippableFact]
    public async Task Expenses_AnonymousRequest_Returns401WrappedEnvelope()
    {
        using var client = Factory.CreateClient(); // no bearer token

        using var listResponse = await client.GetAsync("api/v1/expenses");
        using var createResponse = await client.PostAsJsonAsync("api/v1/expenses", new { name = "Ăn trưa", expenseTime = Noon });

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        using var envelope = await ReadEnvelopeAsync(listResponse);
        AssertErrorEnvelope(envelope, ErrorCodes.Unauthorized);
    }
}
