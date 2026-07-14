namespace FairShareMonApi.Repositories.Abstractions;

/// <summary>
/// Typed query surface a concrete repository exposes for its entity. Implementations delegate to
/// <c>BaseRepository.Query&lt;TEntity&gt;()</c> so AsNoTracking + soft-delete filtering stay the default.
/// </summary>
public interface IQueryRepository<TEntity> where TEntity : class
{
    /// <param name="tracking">Enable change tracking - only when mutating.</param>
    /// <param name="includeDeleted">Include soft-deleted rows (stats/export scenarios).</param>
    IQueryable<TEntity> Query(bool tracking = false, bool includeDeleted = false);
}
