using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Skippable smoke tests for the real-MariaDB harness: connectivity, an AppDbContext bound to
/// the per-test transaction, and rollback isolation. Schema-free on purpose - the model has no
/// entities yet, so everything runs on scalar SELECTs and a session-scoped TEMPORARY table
/// (auto-dropped when the connection closes; never touches real schema or data).
/// </summary>
public class DatabaseFixtureSmokeTest(DatabaseFixture fixture) : IntegrationTestBase(fixture), IClassFixture<DatabaseFixture>
{
    [SkippableFact]
    public async Task Connection_HarnessTransactionOpen_ExecutesScalarSelect()
    {
        await using var command = CreateCommand("SELECT 1");

        var result = await command.ExecuteScalarAsync();

        Assert.Equal(1, Convert.ToInt32(result));
    }

    [SkippableFact]
    public async Task CreateContext_BoundToHarnessTransaction_ExecutesSql()
    {
        await using var context = CreateContext();

        var value = await context.Database.SqlQueryRaw<int>("SELECT 1 AS Value").SingleAsync();

        Assert.Equal(1, value);
    }

    [SkippableFact]
    public async Task Transaction_InsertThenRollback_LeavesNoRowsBehind()
    {
        // Uses its own connection lifecycle: the point is to observe state AFTER a rollback,
        // which the base harness only performs at dispose. TEMPORARY table = session-scoped and
        // schema-free; its InnoDB rows are transactional, so the rollback must erase the insert.
        await using var connection = new MySqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TEMPORARY TABLE fsm_test_rollback_probe (id INT NOT NULL) ENGINE=InnoDB";
            await create.ExecuteNonQueryAsync();
        }

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO fsm_test_rollback_probe (id) VALUES (1)";
                await insert.ExecuteNonQueryAsync();
            }

            await using (var countInside = connection.CreateCommand())
            {
                countInside.Transaction = transaction;
                countInside.CommandText = "SELECT COUNT(*) FROM fsm_test_rollback_probe";
                Assert.Equal(1, Convert.ToInt32(await countInside.ExecuteScalarAsync()));
            }

            await transaction.RollbackAsync();
        }

        await using (var countAfter = connection.CreateCommand())
        {
            countAfter.CommandText = "SELECT COUNT(*) FROM fsm_test_rollback_probe";
            Assert.Equal(0, Convert.ToInt32(await countAfter.ExecuteScalarAsync()));
        }
    }
}
