using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Probes the real MariaDB ONCE per test run (static lazy, shared across all fixture instances).
/// Connection string source, in ascending precedence: the web project's <c>appsettings.json</c>
/// (source file, then the copy next to the test assembly), then the gitignored per-developer
/// <c>appsettings.Development.local.json</c> override (source file, then its copy) if present, then
/// the <c>FSM_TEST_CONNECTION</c> environment variable if set - so a developer's real local MariaDB
/// credentials in <c>.local.json</c> are honored automatically instead of silently falling back to
/// <c>appsettings.json</c>'s placeholder password and reporting a misleading "MariaDB unreachable".
/// Integration tests call <see cref="SkipIfNoDb"/> so they SKIP cleanly instead of failing when the
/// server is genuinely unreachable.
/// </summary>
public sealed class DatabaseFixture
{
    private static readonly Lazy<ProbeResult> Probe = new(ProbeOnce, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when the one-time probe managed to open a connection and run SELECT 1.</summary>
    public bool IsAvailable => Probe.Value.ConnectionString is not null;

    /// <summary>The probed connection string. Only valid after <see cref="SkipIfNoDb"/> passed.</summary>
    public string ConnectionString =>
        Probe.Value.ConnectionString
        ?? throw new InvalidOperationException("MariaDB is unavailable - call SkipIfNoDb() before using the connection.");

    /// <summary>Skips the current [SkippableFact] test when MariaDB is unreachable.</summary>
    public void SkipIfNoDb() =>
        Skip.If(!IsAvailable, $"MariaDB unreachable - integration test skipped. Reason: {Probe.Value.FailureReason}");

    private static ProbeResult ProbeOnce()
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return new ProbeResult(null, "no connection string (FSM_TEST_CONNECTION not set, ConnectionStrings:Default not found in appsettings.json or appsettings.Development.local.json)");

        try
        {
            // AllowUserVariables/UseAffectedRows: Pomelo amends the connection string itself only
            // when it OWNS connection creation. The harness (IntegrationTestBase) hands UseMySql
            // an already-open external MySqlConnection, so the string must carry both flags up
            // front - otherwise Pomelo throws InvalidOperationException on first use.
            var builder = new MySqlConnectionStringBuilder(connectionString)
            {
                ConnectionTimeout = 3,
                AllowUserVariables = true,
                UseAffectedRows = false
            };
            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
            return new ProbeResult(builder.ConnectionString, null);
        }
        catch (Exception exception)
        {
            return new ProbeResult(null, exception.Message);
        }
    }

    private static string? ResolveConnectionString()
    {
        // bin\{Config}\net8.0 -> repo root -> web project's source directory.
        var webProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FairShareMonApi"));

        // Ascending precedence: base appsettings.json (source, then the copy next to the test
        // assembly), then the gitignored per-developer appsettings.Development.local.json override
        // (source, then its copy) - so a developer's real local credentials win over the committed
        // placeholder without needing FSM_TEST_CONNECTION set explicitly every time.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(webProjectDir, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(webProjectDir, "appsettings.Development.local.json"), optional: true)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.local.json"), optional: true)
            .Build();

        var overrideConnection = Environment.GetEnvironmentVariable("FSM_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(overrideConnection))
            return overrideConnection;

        return configuration.GetConnectionString("Default");
    }

    private sealed record ProbeResult(string? ConnectionString, string? FailureReason);
}
