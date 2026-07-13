using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Probes the real MariaDB ONCE per test run (static lazy, shared across all fixture instances).
/// Connection string source: the <c>FSM_TEST_CONNECTION</c> environment variable when set,
/// otherwise the web project's <c>ConnectionStrings:Default</c> (appsettings.json is copied to
/// the test output through the project reference). Integration tests call
/// <see cref="SkipIfNoDb"/> so they SKIP cleanly instead of failing when the server is
/// unreachable.
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
            return new ProbeResult(null, "no connection string (FSM_TEST_CONNECTION not set, ConnectionStrings:Default not found)");

        try
        {
            var builder = new MySqlConnectionStringBuilder(connectionString) { ConnectionTimeout = 3 };
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
        var overrideConnection = Environment.GetEnvironmentVariable("FSM_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(overrideConnection))
            return overrideConnection;

        // Preferred: appsettings.json copied next to the test assembly by the project reference.
        // Fallback: the web project's source file (bin\{Config}\net8.0 -> repo root -> web project).
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FairShareMonApi", "appsettings.json")),
                optional: true)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .Build();

        return configuration.GetConnectionString("Default");
    }

    private sealed record ProbeResult(string? ConnectionString, string? FailureReason);
}
