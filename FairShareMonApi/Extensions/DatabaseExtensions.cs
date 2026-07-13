using FairShareMonApi.Database;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Extensions;

public static class DatabaseExtensions
{
    /// <summary>
    /// Runs a read-only query block. Deliberately does NOT open a database transaction -
    /// reads never need one. The cancellation token is threaded into the delegate so the
    /// query can pass it to EF Core operators.
    /// </summary>
    public static Task<TResult> ExecuteQueryAsync<TContext, TResult>(
        this TContext dbContext,
        Func<TContext, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
        => query(dbContext, cancellationToken);

    /// <summary>
    /// Runs a write block inside a database transaction. Saves and commits unless the delegate
    /// called <see cref="TransactionContext.NoCommit"/>, in which case everything is rolled back.
    /// Do not add a trailing <c>SaveChangesAsync</c> inside the delegate that merely duplicates
    /// this commit - keep explicit saves only for a genuinely needed intermediate flush.
    /// </summary>
    public static async Task<TResult> ExecuteTransactionAsync<TContext, TResult>(
        this TContext dbContext,
        Func<TContext, TransactionContext, Task<TResult>> action,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var transactionContext = new TransactionContext();
            var result = await action(dbContext, transactionContext);

            if (!transactionContext.ShouldCommit)
            {
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
