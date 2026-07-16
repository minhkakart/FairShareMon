using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// End-to-end HTTP culture tests for the Localization subsystem (real MariaDB/Redis, skippable). Proves
/// the full <c>ApiResult</c> envelope - success <c>message</c>, business <c>error.message</c>, validation
/// <c>error.fields[*]</c>, and the auth-handler 401/403 messages - localizes by every culture source
/// (<c>Accept-Language</c> header, <c>?culture=</c> query) with Vietnamese as the neutral default and an
/// unknown culture (fr-FR) folding back to Vietnamese, while the stable numeric <c>error.code</c> never
/// changes. The interpolated Tier-limit case lives in <see cref="LocalizationTierLimitEndpointTests"/>
/// (needs a low-limit host).
/// </summary>
[Collection("AuthIntegration")]
public class LocalizationEndpointTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    private const string Password = "password-8+";

    // ---- send helpers ----------------------------------------------------------------------------

    /// <summary>
    /// Sends a request with an optional <c>Accept-Language</c> header and/or <c>?culture=</c> query. The
    /// caller's <see cref="HttpClient"/> default headers (bearer token, if any) are auto-applied. Returns
    /// the status and the parsed envelope (the caller disposes the <see cref="JsonDocument"/>).
    /// </summary>
    private static async Task<(HttpStatusCode Status, JsonDocument Envelope)> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body = null,
        string? acceptLanguage = null, string? culture = null)
    {
        var fullUrl = culture is null ? url : $"{url}{(url.Contains('?') ? "&" : "?")}culture={culture}";
        using var request = new HttpRequestMessage(method, fullUrl);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (acceptLanguage is not null) request.Headers.Add("Accept-Language", acceptLanguage);
        using var response = await client.SendAsync(request);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
    }

    private static string ErrorMessage(JsonDocument env) =>
        env.RootElement.GetProperty("error").GetProperty("message").GetString()!;

    private static string SuccessMessage(JsonDocument env) =>
        env.RootElement.GetProperty("data").GetProperty("message").GetString()!;

    private static int ErrorCode(JsonDocument env) =>
        env.RootElement.GetProperty("error").GetProperty("code").GetInt32();

    private static string[] FieldMessages(JsonDocument env, string field) =>
        env.RootElement.GetProperty("error").GetProperty("fields").GetProperty(field)
            .EnumerateArray().Select(element => element.GetString()!).ToArray();

    private async Task<HttpClient> RegisterAndLoginFreeAsync()
    {
        var username = NewUsername();
        using (var anonymous = Factory.CreateClient())
        using (var register = await anonymous.PostAsJsonAsync("api/v1/auth/register", new { username, password = Password }))
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        using var loginClient = Factory.CreateClient();
        using var login = await loginClient.PostAsJsonAsync("api/v1/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var envelope = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = envelope.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ---- success message (GET /health) -----------------------------------------------------------

    [SkippableFact]
    public async Task SuccessMessage_Health_LocalizesByEveryCultureSource()
    {
        Fixture.SkipIfNoDb();
        using var client = Factory.CreateClient();

        var (defaultStatus, defaultEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/health");
        using (defaultEnv)
        {
            Assert.Equal(HttpStatusCode.OK, defaultStatus);
            Assert.Equal("Hệ thống hoạt động bình thường.", SuccessMessage(defaultEnv)); // neutral vi-VN
        }

        var (headerStatus, headerEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/health", acceptLanguage: "en-US");
        using (headerEnv)
        {
            Assert.Equal(HttpStatusCode.OK, headerStatus);
            Assert.Equal("The system is operating normally.", SuccessMessage(headerEnv)); // Accept-Language
        }

        var (queryStatus, queryEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/health", culture: "en-US");
        using (queryEnv)
        {
            Assert.Equal(HttpStatusCode.OK, queryStatus);
            Assert.Equal("The system is operating normally.", SuccessMessage(queryEnv)); // ?culture=
        }

        var (unknownStatus, unknownEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/health", acceptLanguage: "fr-FR");
        using (unknownEnv)
        {
            Assert.Equal(HttpStatusCode.OK, unknownStatus);
            Assert.Equal("Hệ thống hoạt động bình thường.", SuccessMessage(unknownEnv)); // unknown -> vi-VN fallback
        }
    }

    // ---- validation failure (error.fields) via POST /auth/register -------------------------------

    [SkippableFact]
    public async Task ValidationFields_Register_LocalizeByCulture()
    {
        Fixture.SkipIfNoDb();
        using var client = Factory.CreateClient();
        var body = new { username = "ab", password = "1234567" }; // too short username + too short password

        var (defaultStatus, defaultEnv) = await SendAsync(client, HttpMethod.Post, "api/v1/auth/register", body);
        using (defaultEnv)
        {
            Assert.Equal(HttpStatusCode.BadRequest, defaultStatus);
            Assert.Equal(ErrorCodes.ValidationFailed, ErrorCode(defaultEnv));
            Assert.Equal("Dữ liệu gửi lên không hợp lệ.", ErrorMessage(defaultEnv));
            Assert.Contains("Tên đăng nhập phải có từ 3 đến 32 ký tự.", FieldMessages(defaultEnv, "username"));
            Assert.Contains("Mật khẩu phải có ít nhất 8 ký tự.", FieldMessages(defaultEnv, "password"));
        }

        var (headerStatus, headerEnv) = await SendAsync(client, HttpMethod.Post, "api/v1/auth/register", body, acceptLanguage: "en-US");
        using (headerEnv)
        {
            Assert.Equal(HttpStatusCode.BadRequest, headerStatus);
            Assert.Equal(ErrorCodes.ValidationFailed, ErrorCode(headerEnv)); // code unchanged across cultures
            Assert.Equal("The submitted data is invalid.", ErrorMessage(headerEnv));
            Assert.Contains("Username must be between 3 and 32 characters.", FieldMessages(headerEnv, "username"));
            Assert.Contains("Password must be at least 8 characters.", FieldMessages(headerEnv, "password"));
        }

        var (queryStatus, queryEnv) = await SendAsync(client, HttpMethod.Post, "api/v1/auth/register", body, culture: "en-US");
        using (queryEnv)
        {
            Assert.Equal(HttpStatusCode.BadRequest, queryStatus);
            Assert.Contains("Username must be between 3 and 32 characters.", FieldMessages(queryEnv, "username")); // ?culture=
        }
    }

    // ---- business ErrorException (login bad credentials) -----------------------------------------

    [SkippableFact]
    public async Task BusinessError_BadLogin_LocalizesMessage_CodeUnchanged()
    {
        Fixture.SkipIfNoDb();
        using var client = Factory.CreateClient();
        var body = new { username = NewUsername(), password = "definitely-wrong" }; // unknown user -> InvalidCredentials

        var (defaultStatus, defaultEnv) = await SendAsync(client, HttpMethod.Post, "api/v1/auth/login", body);
        using (defaultEnv)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, defaultStatus);
            Assert.Equal(ErrorCodes.InvalidCredentials, ErrorCode(defaultEnv));
            Assert.Equal("Tên đăng nhập hoặc mật khẩu không đúng.", ErrorMessage(defaultEnv));
        }

        var (headerStatus, headerEnv) = await SendAsync(client, HttpMethod.Post, "api/v1/auth/login", body, acceptLanguage: "en-US");
        using (headerEnv)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, headerStatus);
            Assert.Equal(ErrorCodes.InvalidCredentials, ErrorCode(headerEnv)); // same numeric contract
            Assert.Equal("Incorrect username or password.", ErrorMessage(headerEnv));
        }
    }

    // ---- OQ11: auth-handler 401 (no token) -------------------------------------------------------

    [SkippableFact]
    public async Task Unauthorized401_NoToken_LocalizesByCulture()
    {
        Fixture.SkipIfNoDb();
        using var client = Factory.CreateClient();

        var (defaultStatus, defaultEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/members");
        using (defaultEnv)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, defaultStatus);
            Assert.Equal(ErrorCodes.Unauthorized, ErrorCode(defaultEnv));
            Assert.Equal("Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", ErrorMessage(defaultEnv));
        }

        var (headerStatus, headerEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/members", acceptLanguage: "en-US");
        using (headerEnv)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, headerStatus);
            Assert.Equal(ErrorCodes.Unauthorized, ErrorCode(headerEnv));
            Assert.Equal("Your session is invalid or has expired.", ErrorMessage(headerEnv));
        }
    }

    // ---- OQ11: auth-handler 403 (non-admin -> /admin route) --------------------------------------

    [SkippableFact]
    public async Task Forbidden403_NonAdmin_LocalizesByCulture()
    {
        Fixture.SkipIfNoDb();
        using var client = await RegisterAndLoginFreeAsync();

        var (defaultStatus, defaultEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/admin/users");
        using (defaultEnv)
        {
            Assert.Equal(HttpStatusCode.Forbidden, defaultStatus);
            Assert.Equal(ErrorCodes.Forbidden, ErrorCode(defaultEnv));
            Assert.Equal("Bạn không có quyền thực hiện thao tác này.", ErrorMessage(defaultEnv));
        }

        var (headerStatus, headerEnv) = await SendAsync(client, HttpMethod.Get, "api/v1/admin/users", acceptLanguage: "en-US");
        using (headerEnv)
        {
            Assert.Equal(HttpStatusCode.Forbidden, headerStatus);
            Assert.Equal(ErrorCodes.Forbidden, ErrorCode(headerEnv));
            Assert.Equal("You do not have permission to perform this action.", ErrorMessage(headerEnv));
        }
    }

    // ---- OQ12: Free-user gated wallet 13003 is FULLY English under en-US -------------------------

    [SkippableFact]
    public async Task PremiumGatedWallet13003_IsFullyEnglishUnderEnUs_AndVietnameseByDefault()
    {
        Fixture.SkipIfNoDb();
        using var client = await RegisterAndLoginFreeAsync();
        var body = new { bankBin = "970436", bankName = "Vietcombank", accountNumber = "0123456789", accountHolderName = "Nguyen Van A" };

        var (defaultStatus, defaultEnv) = await SendAsync(client, HttpMethod.Post, "api/v1/bank-accounts", body);
        using (defaultEnv)
        {
            Assert.Equal(HttpStatusCode.Forbidden, defaultStatus);
            Assert.Equal(ErrorCodes.PremiumFeatureRequired, ErrorCode(defaultEnv));
            var message = ErrorMessage(defaultEnv);
            Assert.Contains("ví ngân hàng", message); // Vietnamese feature name + template
            Assert.Contains("Premium", message);
        }

        var (headerStatus, headerEnv) = await SendAsync(client, HttpMethod.Post, "api/v1/bank-accounts", body, acceptLanguage: "en-US");
        using (headerEnv)
        {
            Assert.Equal(HttpStatusCode.Forbidden, headerStatus);
            Assert.Equal(ErrorCodes.PremiumFeatureRequired, ErrorCode(headerEnv));
            var message = ErrorMessage(headerEnv);
            Assert.Contains("wallet", message);                 // feature name localized (OQ12)
            Assert.Contains("Premium", message);
            Assert.DoesNotContain("ví ngân hàng", message);     // fully English, no mixed Vietnamese
        }
    }
}

/// <summary>
/// The interpolated Free-tier member-limit (13000) message localizing in both cultures, against a host
/// whose <c>Tiers:Free:</c> limits are overridden LOW (2/2/2) so a handful of rows hit the cap. Proves
/// the <c>{0}</c> limit number renders and the surrounding template is Vietnamese by default / English
/// under <c>Accept-Language: en-US</c>, with the numeric <c>error.code</c> unchanged.
/// </summary>
[Collection("AuthIntegration")]
public class LocalizationTierLimitEndpointTests(TierLimitWebApplicationFactory factory, DatabaseFixture fixture)
    : TierEndpointTestBase(factory, fixture), IClassFixture<TierLimitWebApplicationFactory>, IClassFixture<DatabaseFixture>
{
    private static async Task<(HttpStatusCode Status, JsonDocument Envelope)> PostMemberAsync(
        HttpClient client, string name, string? acceptLanguage)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/members")
        {
            Content = JsonContent.Create(new { name })
        };
        if (acceptLanguage is not null) request.Headers.Add("Accept-Language", acceptLanguage);
        using var response = await client.SendAsync(request);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
    }

    [SkippableFact]
    public async Task MemberLimit13000_InterpolatedMessage_LocalizesInBothCultures()
    {
        var (client, _) = await CreateFreeClientAsync();

        // Owner-rep occupies 1 of the 2 slots; this fills the 2nd, leaving the account exactly at the cap.
        var (fillStatus, fillEnv) = await PostMemberAsync(client, "An", null);
        using (fillEnv) Assert.Equal(HttpStatusCode.OK, fillStatus);

        var (enStatus, enEnv) = await PostMemberAsync(client, "Binh", "en-US");
        using (enEnv)
        {
            Assert.Equal(HttpStatusCode.BadRequest, enStatus);
            AssertErrorEnvelope(enEnv, ErrorCodes.MemberLimitReached);
            var message = enEnv.RootElement.GetProperty("error").GetProperty("message").GetString()!;
            Assert.Contains("2", message);                   // interpolated configured limit
            Assert.Contains("Upgrade to Premium", message);  // English template
        }

        var (viStatus, viEnv) = await PostMemberAsync(client, "Binh", null);
        using (viEnv)
        {
            Assert.Equal(HttpStatusCode.BadRequest, viStatus);
            AssertErrorEnvelope(viEnv, ErrorCodes.MemberLimitReached);
            var message = viEnv.RootElement.GetProperty("error").GetProperty("message").GetString()!;
            Assert.Contains("2", message);                   // interpolated configured limit
            Assert.Contains("Nâng cấp Premium", message);    // Vietnamese template (default)
        }

        client.Dispose();
    }
}
