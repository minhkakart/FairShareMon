namespace FairShareMonApi.Models.BankCallbacks;

/// <summary>Root config section ("BankCallbacks") for every bank-transaction webhook provider (planning/bank-callback-settlement.md Step 7/9).</summary>
public class BankCallbacksOptions
{
    public const string SectionName = "BankCallbacks";

    public SePayCallbackOptions SePay { get; set; } = new();
}

/// <summary>SePay-specific webhook config (Assumptions: a static API key header, verified constant-time).</summary>
public class SePayCallbackOptions
{
    /// <summary>The configured secret compared against the request's <c>Authorization: Apikey {key}</c> header. Blank/missing always fails closed (never "no key configured = allow").</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The correlation-code prefix this provider's fallback regex looks for in the transfer content (mirrors <see cref="Database.Entities.QrCorrelationCode.CodePrefix"/>).</summary>
    public string CodePrefix { get; set; } = "FSM";
}
