using System.Text.Json;
using DiDecoration.Attributes;
using FairShareMonApi.Database.Entities;

namespace FairShareMonApi.Services.Audit;

/// <summary>
/// Pure builder for immutable <see cref="AuditLog"/> rows (OQ20) - no DB, no transaction. The
/// expense/share repositories call it INSIDE their <c>ExecuteTransactionAsync</c> and stage the
/// result via <c>db.AuditLogs.Add(...)</c>, so the audit shares the mutation's fate (§3.8 "thao tác
/// thất bại thì không có log"). Snapshots are serialized with denormalized names (OQ10); an Update
/// whose before/after snapshots serialize equal is a no-op and returns <c>null</c> (no row, OQ9).
/// </summary>
public interface IAuditLogFactory
{
    /// <summary>Builds an expense audit row. Returns null only for a no-op Update (equal snapshots).</summary>
    AuditLog? BuildExpenseAudit(AuditAction action, ExpenseAuditSnapshot? before, ExpenseAuditSnapshot? after, ulong actorUserId);

    /// <summary>Builds a share audit row. Returns null only for a no-op Update (equal snapshots).</summary>
    AuditLog? BuildShareAudit(AuditAction action, ShareAuditSnapshot? before, ShareAuditSnapshot? after, ulong actorUserId);
}

[ScopedService(typeof(IAuditLogFactory))]
public sealed class AuditLogFactory : IAuditLogFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditLog? BuildExpenseAudit(AuditAction action, ExpenseAuditSnapshot? before, ExpenseAuditSnapshot? after, ulong actorUserId)
    {
        var beforeJson = Serialize(before);
        var afterJson = Serialize(after);

        // No-op edit (equal snapshots) produces no log (OQ9).
        if (action == AuditAction.Update && beforeJson == afterJson)
            return null;

        var entityUuid = (after ?? before)!.Uuid;
        return new AuditLog
        {
            ActorUserId = actorUserId,
            EntityType = AuditEntityType.Expense,
            EntityUuid = entityUuid,
            ExpenseUuid = entityUuid,
            Action = action,
            BeforeData = beforeJson,
            AfterData = afterJson
        };
    }

    public AuditLog? BuildShareAudit(AuditAction action, ShareAuditSnapshot? before, ShareAuditSnapshot? after, ulong actorUserId)
    {
        var beforeJson = Serialize(before);
        var afterJson = Serialize(after);

        if (action == AuditAction.Update && beforeJson == afterJson)
            return null;

        var snapshot = (after ?? before)!;
        return new AuditLog
        {
            ActorUserId = actorUserId,
            EntityType = AuditEntityType.Share,
            EntityUuid = snapshot.Uuid,
            ExpenseUuid = snapshot.ExpenseUuid,
            Action = action,
            BeforeData = beforeJson,
            AfterData = afterJson
        };
    }

    private static string? Serialize<T>(T? snapshot) where T : class =>
        snapshot is null ? null : JsonSerializer.Serialize(snapshot, SerializerOptions);
}
