using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Read access for the immutable <see cref="AuditLog"/> (§3.8). The per-expense history is scoped by
/// <c>actor_user_id</c> + <c>expense_uuid</c> on <c>audit_logs</c> - NOT by the (possibly
/// hard-deleted) expense row - so a deleted-but-owned expense still returns its history and a
/// foreign/unknown uuid returns an empty list (OQ17). The write path is the <c>AuditLogFactory</c>
/// staged inside the expense/share mutation transactions; this repository never writes.
/// </summary>
public interface IAuditLogRepository : IBaseRepository
{
    /// <summary>Audit rows for the user+expense_uuid, ordered created_at ASC (then id for determinism). Empty when none (OQ17).</summary>
    Task<IReadOnlyList<AuditLog>> ListByExpenseAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IAuditLogRepository))]
public sealed class AuditLogRepository(AppDbContext dbContext) : BaseRepository(dbContext), IAuditLogRepository
{
    public Task<IReadOnlyList<AuditLog>> ListByExpenseAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var logs = await Query<AuditLog>()
                .Where(log => log.ActorUser.Uuid == userUuid && log.ExpenseUuid == expenseUuid)
                .OrderBy(log => log.CreatedAt)
                .ThenBy(log => log.Id)
                .ToListAsync(ct);
            return (IReadOnlyList<AuditLog>)logs;
        }, cancellationToken);
}
