using System.Data.Common;
using FairShareMonApi.Database;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Base class for integration tests against the real MariaDB. Per test: skips when the DB is
/// unreachable (<see cref="DatabaseFixture.SkipIfNoDb"/>), opens one real connection, begins a
/// transaction, and builds <see cref="DbContextOptions{AppDbContext}"/> bound to that same
/// connection (Pomelo, pinned MariaDbServerVersion 11.7.2). The transaction is ROLLED BACK on
/// dispose, so the real database is never dirtied. Derived classes must also declare
/// <c>IClassFixture&lt;DatabaseFixture&gt;</c> and mark tests <c>[SkippableFact]</c>.
/// </summary>
public abstract class IntegrationTestBase(DatabaseFixture fixture) : IAsyncLifetime
{
    protected DatabaseFixture Fixture { get; } = fixture;

    protected MySqlConnection Connection { get; private set; } = null!;

    protected DbTransaction Transaction { get; private set; } = null!;

    protected DbContextOptions<AppDbContext> ContextOptions { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Fixture.SkipIfNoDb();

        Connection = new MySqlConnection(Fixture.ConnectionString);
        await Connection.OpenAsync();
        Transaction = await Connection.BeginTransactionAsync();

        ContextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(Connection, new MariaDbServerVersion(new Version(11, 7, 2)))
            .Options;
    }

    /// <summary>Creates an <see cref="AppDbContext"/> enlisted in the per-test transaction.</summary>
    protected AppDbContext CreateContext()
    {
        var context = new AppDbContext(ContextOptions);
        context.Database.UseTransaction(Transaction);
        return context;
    }

    /// <summary>
    /// Creates a raw command bound to the per-test transaction (MySqlConnector rejects commands
    /// that ignore the connection's active transaction).
    /// </summary>
    protected MySqlCommand CreateCommand(string sql)
    {
        var command = Connection.CreateCommand();
        command.Transaction = (MySqlTransaction)Transaction;
        command.CommandText = sql;
        return command;
    }

    public async Task DisposeAsync()
    {
        // Members stay null when InitializeAsync skipped before opening anything.
        if (Transaction is not null)
        {
            await Transaction.RollbackAsync();
            await Transaction.DisposeAsync();
        }

        if (Connection is not null)
            await Connection.DisposeAsync();
    }
}
