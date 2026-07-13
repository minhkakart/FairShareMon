using FairShareMonApi.Database;
using FairShareMonApi.Database.Abstractions;
using FairShareMonApi.Extensions;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories.Abstractions;

/// <summary>
/// Base class for repositories. Reads go through <see cref="ExecuteQueryAsync{TResult}"/> and
/// <see cref="Query{TEntity}"/> (AsNoTracking + soft-delete filtering by default); writes go
/// through <see cref="ExecuteTransactionAsync{TResult}"/>.
/// </summary>
public abstract class BaseRepository(AppDbContext dbContext) : IBaseRepository
{
    protected AppDbContext DbContext => dbContext;

    public Task<TResult> ExecuteQueryAsync<TResult>(
        Func<AppDbContext, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default) =>
        dbContext.ExecuteQueryAsync(query, cancellationToken);

    public Task<TResult> ExecuteTransactionAsync<TResult>(
        Func<AppDbContext, TransactionContext, Task<TResult>> action,
        CancellationToken cancellationToken = default) =>
        dbContext.ExecuteTransactionAsync(action, cancellationToken);

    /// <summary>
    /// Entity query applying <c>AsNoTracking</c> (unless <paramref name="tracking"/>) and
    /// excluding soft-deleted rows for <see cref="IEntityDeletable"/> entities (unless
    /// <paramref name="includeDeleted"/>).
    /// </summary>
    protected IQueryable<TEntity> Query<TEntity>(bool tracking = false, bool includeDeleted = false)
        where TEntity : class
    {
        IQueryable<TEntity> query = dbContext.Set<TEntity>();

        if (!tracking)
            query = query.AsNoTracking();

        if (!includeDeleted && typeof(IEntityDeletable).IsAssignableFrom(typeof(TEntity)))
            query = query.Where(entity => !EF.Property<bool>(entity, nameof(IEntityDeletable.IsDeleted)));

        return query;
    }
}
