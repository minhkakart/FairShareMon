using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Repositories.Stats;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Read-only aggregation data access for the per-event debt balance (§3.7) and the overview /
/// by-category statistics (§3.9) - M7. Every read is resource-owned: scoped by the owning user's UUID
/// so another user's rows never leak (an event ownership miss yields null, mapped to EventNotFound by
/// the service). All figures are computed <b>DB-side</b> via <c>GROUP BY</c>/<c>SUM</c>/<c>COUNT</c>
/// pushed into MariaDB (OQ11): each <c>GroupBy(...).Select(g =&gt; g.Sum/Count)</c> translates to a SQL
/// aggregate; only the small per-member / per-category stitch + the display-info join run in memory
/// (never <c>Include</c>-then-sum-in-memory). No writes, no transactions, no schema change.
/// </summary>
public interface IStatsRepository : IBaseRepository
{
    /// <summary>Resolve + own the event (for the balance/by-category header and the 404 scope). Null on an ownership miss.</summary>
    Task<Event?> FindOwnedEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-member advanced/owed for an event (§3.7). Advanced and owed are resolved from the SAME single
    /// share-set - the event's expenses' shares - grouped by <c>payer_member_id</c> and by
    /// <c>member_id</c> respectively, so <c>Σ advanced == Σ owed</c> and the balances sum to zero by
    /// construction (OQ1). Includes every participant: owner-rep (even at 0đ) and soft-deleted members
    /// (OQ3/§4.7). An event with no expenses yields an empty list (OQ15).
    /// </summary>
    Task<IReadOnlyList<MemberBalanceAggregate>> GetEventBalanceAsync(string userUuid, ulong eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overview totals over the owner's whole ledger (loose + event expenses) in an inclusive
    /// <c>[from,to]</c> UTC range, either bound optional (OQ6/OQ7). Total spending = <c>SUM(share.amount)</c>;
    /// expense count = distinct expenses in range. An empty range yields zeros.
    /// </summary>
    Task<OverviewAggregate> GetOverviewAsync(string userUuid, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-category total + expense count in scope (§3.9). Scope is a time range OR a single owned event
    /// (<paramref name="eventId"/> wins when set - OQ8). Only categories with ≥1 in-scope expense appear,
    /// including soft-deleted categories with historical expenses (OQ9/§4.7); rows are sorted
    /// <c>total</c> DESC → <c>expenseCount</c> DESC → <c>name</c>. Empty scope yields an empty list.
    /// </summary>
    Task<IReadOnlyList<CategoryStatAggregate>> GetByCategoryAsync(string userUuid, DateTime? from, DateTime? to, ulong? eventId, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IStatsRepository))]
public sealed class StatsRepository(AppDbContext dbContext) : BaseRepository(dbContext), IStatsRepository
{
    public Task<Event?> FindOwnedEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query<Event>()
            .FirstOrDefaultAsync(evt => evt.Uuid == eventUuid && evt.User.Uuid == userUuid, ct), cancellationToken);

    public Task<IReadOnlyList<MemberBalanceAggregate>> GetEventBalanceAsync(string userUuid, ulong eventId, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            // The single share-set: every share of the event's expenses (resource-owned defense-in-depth).
            var shares = Query<Share>().Where(share =>
                share.Expense.EventId == eventId && share.Expense.User.Uuid == userUuid);

            // Advanced per payer (DB-side SUM grouped by the expense's payer).
            var advancedByPayer = await shares
                .GroupBy(share => share.Expense.PayerMemberId)
                .Select(group => new { MemberId = group.Key, Amount = group.Sum(share => share.Amount) })
                .ToListAsync(ct);

            // Owed per member (DB-side SUM grouped by the share's member) - SAME share-set (OQ1).
            var owedByMember = await shares
                .GroupBy(share => share.MemberId)
                .Select(group => new { MemberId = group.Key, Amount = group.Sum(share => share.Amount) })
                .ToListAsync(ct);

            var advancedMap = advancedByPayer.ToDictionary(row => row.MemberId, row => row.Amount);
            var owedMap = owedByMember.ToDictionary(row => row.MemberId, row => row.Amount);

            var memberIds = advancedMap.Keys.Union(owedMap.Keys).ToList();
            if (memberIds.Count == 0)
                return (IReadOnlyList<MemberBalanceAggregate>)Array.Empty<MemberBalanceAggregate>();

            // Display info incl. soft-deleted members (OQ3/§4.7).
            var members = await Query<Member>(includeDeleted: true)
                .Where(member => memberIds.Contains(member.Id))
                .Select(member => new { member.Id, member.Uuid, member.Name, member.IsOwnerRepresentative, member.IsDeleted })
                .ToListAsync(ct);

            // Layer B overlay flags (settled-per-member OQ8a): additive load, keyed by member_id. This does
            // NOT touch advanced/owed/balance above - the balance stays pure (D2 / M7 OQ2).
            var settlements = await Query<EventMemberSettlement>()
                .Where(settlement => settlement.EventId == eventId)
                .Select(settlement => new { settlement.MemberId, settlement.IsSettled, settlement.SettledAt })
                .ToListAsync(ct);
            var settledMap = settlements.ToDictionary(row => row.MemberId, row => (row.IsSettled, row.SettledAt));

            var rows = members
                .Select(member =>
                {
                    var settled = settledMap.TryGetValue(member.Id, out var flag) ? flag : (IsSettled: false, SettledAt: (DateTime?)null);
                    return new MemberBalanceAggregate(
                        member.Uuid,
                        member.Name,
                        member.IsOwnerRepresentative,
                        member.IsDeleted,
                        advancedMap.GetValueOrDefault(member.Id, 0m),
                        owedMap.GetValueOrDefault(member.Id, 0m),
                        settled.IsSettled,
                        settled.SettledAt);
                })
                .OrderByDescending(row => row.Advanced - row.Owed)
                .ThenBy(row => row.MemberName)
                .ToList();
            return (IReadOnlyList<MemberBalanceAggregate>)rows;
        }, cancellationToken);

    public Task<OverviewAggregate> GetOverviewAsync(string userUuid, DateTime? from, DateTime? to, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            // Expense count: distinct expenses in the owner's ledger in range (DB-side COUNT).
            var expenses = Query<Expense>().Where(expense => expense.User.Uuid == userUuid);
            if (from.HasValue)
                expenses = expenses.Where(expense => expense.ExpenseTime >= from.Value);
            if (to.HasValue)
                expenses = expenses.Where(expense => expense.ExpenseTime <= to.Value);
            var expenseCount = await expenses.CountAsync(ct);

            // Total spending = SUM(share.amount) over the same expense scope (DB-side SUM).
            var shares = Query<Share>().Where(share => share.Expense.User.Uuid == userUuid);
            if (from.HasValue)
                shares = shares.Where(share => share.Expense.ExpenseTime >= from.Value);
            if (to.HasValue)
                shares = shares.Where(share => share.Expense.ExpenseTime <= to.Value);
            var totalSpending = await shares.SumAsync(share => (decimal?)share.Amount, ct) ?? 0m;

            return new OverviewAggregate(totalSpending, expenseCount);
        }, cancellationToken);

    public Task<IReadOnlyList<CategoryStatAggregate>> GetByCategoryAsync(string userUuid, DateTime? from, DateTime? to, ulong? eventId, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            // Expense scope: event mode (eventId wins) OR the inclusive time range (OQ8).
            var expenses = Query<Expense>().Where(expense => expense.User.Uuid == userUuid);
            if (eventId.HasValue)
            {
                expenses = expenses.Where(expense => expense.EventId == eventId.Value);
            }
            else
            {
                if (from.HasValue)
                    expenses = expenses.Where(expense => expense.ExpenseTime >= from.Value);
                if (to.HasValue)
                    expenses = expenses.Where(expense => expense.ExpenseTime <= to.Value);
            }

            // Count per category over the in-scope expenses (DB-side COUNT). Categories with ≥1 expense (OQ9).
            var countByCategory = await expenses
                .GroupBy(expense => expense.CategoryId)
                .Select(group => new { CategoryId = group.Key, Count = group.Count() })
                .ToListAsync(ct);

            if (countByCategory.Count == 0)
                return (IReadOnlyList<CategoryStatAggregate>)Array.Empty<CategoryStatAggregate>();

            // Total per category = SUM over the shares of the in-scope expenses (DB-side SUM).
            var shares = Query<Share>().Where(share => share.Expense.User.Uuid == userUuid);
            if (eventId.HasValue)
            {
                shares = shares.Where(share => share.Expense.EventId == eventId.Value);
            }
            else
            {
                if (from.HasValue)
                    shares = shares.Where(share => share.Expense.ExpenseTime >= from.Value);
                if (to.HasValue)
                    shares = shares.Where(share => share.Expense.ExpenseTime <= to.Value);
            }

            var totalByCategory = await shares
                .GroupBy(share => share.Expense.CategoryId)
                .Select(group => new { CategoryId = group.Key, Total = group.Sum(share => share.Amount) })
                .ToListAsync(ct);

            var countMap = countByCategory.ToDictionary(row => row.CategoryId, row => row.Count);
            var totalMap = totalByCategory.ToDictionary(row => row.CategoryId, row => row.Total);
            var categoryIds = countMap.Keys.ToList();

            // Display info incl. soft-deleted categories with historical expenses (OQ9/§4.7).
            var categories = await Query<Category>(includeDeleted: true)
                .Where(category => categoryIds.Contains(category.Id))
                .Select(category => new { category.Id, category.Uuid, category.Name, category.Color, category.Icon, category.IsDeleted })
                .ToListAsync(ct);

            var rows = categories
                .Select(category => new CategoryStatAggregate(
                    category.Uuid,
                    category.Name,
                    category.Color,
                    category.Icon,
                    category.IsDeleted,
                    totalMap.GetValueOrDefault(category.Id, 0m),
                    countMap.GetValueOrDefault(category.Id, 0)))
                .OrderByDescending(row => row.Total)
                .ThenByDescending(row => row.ExpenseCount)
                .ThenBy(row => row.CategoryName)
                .ToList();
            return (IReadOnlyList<CategoryStatAggregate>)rows;
        }, cancellationToken);
}
