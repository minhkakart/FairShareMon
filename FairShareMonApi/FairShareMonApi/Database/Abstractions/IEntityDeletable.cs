namespace FairShareMonApi.Database.Abstractions;

/// <summary>
/// Soft-delete contract: rows with <c>IsDeleted = true</c> are excluded by
/// <c>BaseRepository.Query</c> by default (pass <c>includeDeleted: true</c> to see them).
/// </summary>
public interface IEntityDeletable
{
    bool IsDeleted { get; set; }
}
