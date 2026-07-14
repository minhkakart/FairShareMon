using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Probes Redis ONCE per test run, mirroring <see cref="DatabaseFixture"/>. Connection string from
/// the <c>FSM_TEST_REDIS</c> environment variable when set, otherwise the web project's
/// <c>Redis:Configuration</c>. Tests that require a LIVE Redis (cache-first behavior, backfill)
/// call <see cref="SkipIfNoRedis"/>; tests of the warn-and-continue degradation path use
/// <see cref="UnreachableRedis"/> instead and never skip.
/// </summary>
public sealed class RedisFixture
{
    private static readonly Lazy<ProbeResult> Probe = new(ProbeOnce, LazyThreadSafetyMode.ExecutionAndPublication);

    public bool IsAvailable => Probe.Value.Redis is not null;

    /// <summary>Shared live multiplexer. Only valid after <see cref="SkipIfNoRedis"/> passed.</summary>
    public IConnectionMultiplexer Redis =>
        Probe.Value.Redis
        ?? throw new InvalidOperationException("Redis is unavailable - call SkipIfNoRedis() before using it.");

    public void SkipIfNoRedis() =>
        Skip.If(!IsAvailable, $"Redis unreachable - integration test skipped. Reason: {Probe.Value.FailureReason}");

    private static ProbeResult ProbeOnce()
    {
        var configurationString = ResolveConfigurationString();
        if (string.IsNullOrWhiteSpace(configurationString))
            return new ProbeResult(null, "no Redis configuration (FSM_TEST_REDIS not set, Redis:Configuration not found)");

        try
        {
            var options = ConfigurationOptions.Parse(configurationString);
            options.AbortOnConnectFail = true; // probing - fail fast instead of lazy reconnect
            options.ConnectTimeout = 2000;
            options.ConnectRetry = 1;

            var redis = ConnectionMultiplexer.Connect(options);
            redis.GetDatabase().Ping();
            return new ProbeResult(redis, null); // kept for the whole run, like the DB probe
        }
        catch (Exception exception)
        {
            return new ProbeResult(null, exception.Message);
        }
    }

    private static string? ResolveConfigurationString()
    {
        var overrideConfiguration = Environment.GetEnvironmentVariable("FSM_TEST_REDIS");
        if (!string.IsNullOrWhiteSpace(overrideConfiguration))
            return overrideConfiguration;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FairShareMonApi", "appsettings.json")),
                optional: true)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .Build();

        return configuration.GetValue<string>("Redis:Configuration");
    }

    private sealed record ProbeResult(IConnectionMultiplexer? Redis, string? FailureReason);
}

/// <summary>
/// A multiplexer pointed at a port nothing listens on (<c>abortConnect=false</c>, so construction
/// succeeds and every operation throws) - used to exercise the OQ7 "Redis best-effort,
/// warn-and-continue, DB is the source of truth" degradation paths deterministically.
/// </summary>
public static class UnreachableRedis
{
    private static readonly Lazy<IConnectionMultiplexer> Lazy = new(
        () => ConnectionMultiplexer.Connect(
            "localhost:1,abortConnect=false,connectTimeout=200,connectRetry=0,syncTimeout=250,asyncTimeout=250"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IConnectionMultiplexer Instance => Lazy.Value;
}
