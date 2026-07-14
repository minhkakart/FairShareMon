using FairShareMonApi.Database;

namespace FairShareMonApi.Repositories.Abstractions;

/// <summary>
/// Common data-access surface: <c>ExecuteQueryAsync</c> for reads (no transaction),
/// <c>ExecuteTransactionAsync</c> for writes (commit unless <c>TransactionContext.NoCommit()</c>).
/// </summary>
public interface IBaseRepository
{
    Task<TResult> ExecuteQueryAsync<TResult>(
        Func<AppDbContext, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteTransactionAsync<TResult>(
        Func<AppDbContext, TransactionContext, Task<TResult>> action,
        CancellationToken cancellationToken = default);
}
