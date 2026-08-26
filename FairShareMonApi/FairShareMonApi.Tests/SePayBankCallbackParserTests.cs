using System.Text.Json;
using FairShareMonApi.Models.BankCallbacks;
using FairShareMonApi.Services.Api.BankCallbacks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="SePayBankCallbackParser"/> (no DB, no HTTP) - planning/
/// bank-callback-settlement.md Step 10. Proves <c>Verify</c>'s constant-time header check (exact
/// configured value accepted; missing/wrong/blank-configured-key all rejected, fail-closed) and
/// <c>Parse</c>'s field mapping, the <c>transferType != "in"</c> -&gt; <c>IsIncoming = false</c> rule, and
/// the code-extraction precedence (SePay's own pre-extracted <c>code</c> field wins over the app-side
/// regex fallback over <c>content</c>; no match anywhere -&gt; null).
/// </summary>
public class SePayBankCallbackParserTests
{
    private const string ConfiguredApiKey = "test-sepay-secret-key";

    private static SePayBankCallbackParser CreateParser(string apiKey = ConfiguredApiKey, string codePrefix = "FSM") =>
        new(Options.Create(new BankCallbacksOptions { SePay = new SePayCallbackOptions { ApiKey = apiKey, CodePrefix = codePrefix } }));

    private static HttpRequest RequestWithAuthorizationHeader(string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
            context.Request.Headers["Authorization"] = headerValue;
        return context.Request;
    }

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    // The Assumptions section's own sample SePay payload.
    private const string SamplePayload = """
    {
      "id": 92704,
      "gateway": "Vietcombank",
      "transactionDate": "2026-08-26 14:02:37",
      "accountNumber": "0123499999",
      "code": null,
      "content": "FSM8K2QX7 chuyen tien",
      "transferType": "in",
      "transferAmount": 500000,
      "accumulated": 19077000,
      "subAccount": null,
      "referenceCode": "MBVCB.3278907687",
      "description": ""
    }
    """;

    // ---- Verify ------------------------------------------------------------------------------------

    [Fact]
    public void Verify_ExactConfiguredHeaderValue_ReturnsTrue()
    {
        var request = RequestWithAuthorizationHeader($"Apikey {ConfiguredApiKey}");

        Assert.True(CreateParser().Verify(request, Payload(SamplePayload)));
    }

    [Fact]
    public void Verify_MissingHeader_ReturnsFalse()
    {
        var request = RequestWithAuthorizationHeader(null);

        Assert.False(CreateParser().Verify(request, Payload(SamplePayload)));
    }

    [Fact]
    public void Verify_WrongKeyValue_ReturnsFalse()
    {
        var request = RequestWithAuthorizationHeader("Apikey wrong-key");

        Assert.False(CreateParser().Verify(request, Payload(SamplePayload)));
    }

    [Fact]
    public void Verify_WrongScheme_ReturnsFalse()
    {
        var request = RequestWithAuthorizationHeader($"Bearer {ConfiguredApiKey}");

        Assert.False(CreateParser().Verify(request, Payload(SamplePayload)));
    }

    [Fact]
    public void Verify_BlankConfiguredKey_AlwaysFailsClosed()
    {
        // Even a header that "looks right" (empty scheme+value) must not pass when nothing is configured -
        // never "no key configured = allow" (Assumptions/Step 4).
        var request = RequestWithAuthorizationHeader("Apikey ");

        Assert.False(CreateParser(apiKey: "").Verify(request, Payload(SamplePayload)));
    }

    [Fact]
    public void Verify_ConfiguredKeyIsWhitespaceOnly_AlwaysFailsClosed()
    {
        var request = RequestWithAuthorizationHeader("Apikey    ");

        Assert.False(CreateParser(apiKey: "   ").Verify(request, Payload(SamplePayload)));
    }

    // ---- Parse: field mapping ------------------------------------------------------------------------

    [Fact]
    public void Parse_MapsEveryField()
    {
        var result = CreateParser().Parse(Payload(SamplePayload));

        Assert.NotNull(result);
        Assert.Equal("92704", result!.ProviderTransactionId);
        Assert.True(result.IsIncoming);
        Assert.Equal(500_000m, result.Amount);
        Assert.Equal("FSM8K2QX7 chuyen tien", result.Content);
        Assert.Equal("FSM8K2QX7", result.ExtractedCode); // SePay's own "code" is null here -> regex fallback
        Assert.Equal(new DateTime(2026, 8, 26, 14, 2, 37, DateTimeKind.Utc), result.TransactionAt);
        Assert.Equal("0123499999", result.DestinationAccountNumber);
        Assert.Null(result.BankBin); // SePay's sample payload carries no BIN field - never set by this parser
    }

    [Fact]
    public void Parse_IdAsJsonStringInsteadOfNumber_StillParses()
    {
        const string json = """{"id":"92705","transferType":"in","transferAmount":100000,"content":"hi"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.NotNull(result);
        Assert.Equal("92705", result!.ProviderTransactionId);
    }

    [Fact]
    public void Parse_MissingIdField_ReturnsNull()
    {
        const string json = """{"transferType":"in","transferAmount":100000,"content":"hi"}""";

        Assert.Null(CreateParser().Parse(Payload(json)));
    }

    [Fact]
    public void Parse_TransferAmountAsJsonString_StillParses()
    {
        const string json = """{"id":1,"transferType":"in","transferAmount":"250000","content":"hi"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.NotNull(result);
        Assert.Equal(250_000m, result!.Amount);
    }

    // ---- Parse: transferType filtering ----------------------------------------------------------------

    [Fact]
    public void Parse_TransferTypeOut_SetsIsIncomingFalse()
    {
        const string json = """{"id":1,"transferType":"out","transferAmount":100000,"content":"hi"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.NotNull(result);
        Assert.False(result!.IsIncoming);
    }

    [Fact]
    public void Parse_TransferTypeInDifferentCasing_StillIncoming()
    {
        const string json = """{"id":1,"transferType":"IN","transferAmount":100000,"content":"hi"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.NotNull(result);
        Assert.True(result!.IsIncoming);
    }

    [Fact]
    public void Parse_TransferTypeMissing_IsIncomingFalse()
    {
        const string json = """{"id":1,"transferAmount":100000,"content":"hi"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.NotNull(result);
        Assert.False(result!.IsIncoming);
    }

    // ---- Parse: code-field-vs-regex-fallback extraction ------------------------------------------------

    [Fact]
    public void Parse_PayloadOwnCodeField_PreferredOverRegexFallback()
    {
        // content ALSO contains a code-shaped substring, but the payload's own "code" field must win.
        const string json = """{"id":1,"transferType":"in","transferAmount":100000,"code":"FSMABC234","content":"chuyen tien FSMZZZZZZ ref"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.Equal("FSMABC234", result!.ExtractedCode);
    }

    [Fact]
    public void Parse_CodeFieldNull_FallsBackToRegexOverContent()
    {
        const string json = """{"id":1,"transferType":"in","transferAmount":100000,"code":null,"content":"chuyen tien FSM8K2QX7 abc"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.Equal("FSM8K2QX7", result!.ExtractedCode);
    }

    [Fact]
    public void Parse_CodeFieldBlank_FallsBackToRegexOverContent()
    {
        const string json = """{"id":1,"transferType":"in","transferAmount":100000,"code":"  ","content":"FSM8K2QX7 chuyen tien"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.Equal("FSM8K2QX7", result!.ExtractedCode);
    }

    [Fact]
    public void Parse_NoCodeFieldAndNoRegexMatch_ExtractedCodeNull()
    {
        const string json = """{"id":1,"transferType":"in","transferAmount":100000,"content":"chuyen tien khong co ma gi ca"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.Null(result!.ExtractedCode);
    }

    [Fact]
    public void Parse_RegexRespectsConfiguredPrefix_NotJustHardcodedFsm()
    {
        // A different configured prefix must be honoured by the fallback regex.
        const string json = """{"id":1,"transferType":"in","transferAmount":100000,"content":"ABC8K2QX7 chuyen tien"}""";

        var result = CreateParser(codePrefix: "ABC").Parse(Payload(json));

        Assert.Equal("ABC8K2QX7", result!.ExtractedCode);
    }

    [Fact]
    public void Parse_RegexRequiresExactlySixTrailingChars_ShorterRunDoesNotMatch()
    {
        const string json = """{"id":1,"transferType":"in","transferAmount":100000,"content":"chuyen tien FSM8K2 abc"}""";

        var result = CreateParser().Parse(Payload(json));

        Assert.Null(result!.ExtractedCode);
    }
}
