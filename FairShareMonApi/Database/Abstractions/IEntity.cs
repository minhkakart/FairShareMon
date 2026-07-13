namespace FairShareMonApi.Database.Abstractions;

/// <summary>
/// Base contract for persisted entities: bigint-unsigned PK, a time-ordered UUIDv7 string
/// (generated via <c>Uuid.NewV7()</c>, unique-indexed) used for all external references, and
/// audit timestamps (see Entity Conventions, .agents/rules/rules.md).
/// </summary>
public interface IEntity
{
    ulong Id { get; set; }

    string Uuid { get; set; }

    DateTime CreatedAt { get; set; }

    DateTime UpdatedAt { get; set; }
}
