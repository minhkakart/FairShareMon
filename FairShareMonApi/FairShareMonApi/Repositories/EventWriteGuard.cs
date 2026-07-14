using FairShareMonApi.Database.Entities;

namespace FairShareMonApi.Repositories;

/// <summary>
/// The closed-event write block (§4.4, OQ13): a shared repository-layer guard invoked inside each M5
/// write transaction, immediately after the tracked expense (with its <see cref="Expense.Event"/>
/// navigation Included) is loaded. When the expense's current event is CLOSED the caller must abort
/// the transaction (<c>NoCommit</c>) and return <c>EventClosed</c> (9001). Woven into
/// <c>ExpenseRepository.UpdateGeneralInfoAsync</c>/<c>DeleteAsync</c>/<c>AssignEventAsync</c>/
/// <c>RemoveEventAsync</c> and <c>ShareRepository.AddAsync</c>/<c>UpdateAsync</c>/<c>DeleteAsync</c>.
/// <c>ExpenseRepository.SetSettledAsync</c> deliberately does NOT invoke it - the sole §4.4 exception.
/// </summary>
public static class EventWriteGuard
{
    /// <summary>
    /// True when the expense belongs to a CLOSED event, so every write except the settled flag must be
    /// rejected. Requires the <see cref="Expense.Event"/> navigation to be loaded (via <c>Include</c>);
    /// a loose expense (no event) is never blocked.
    /// </summary>
    public static bool IsCurrentEventClosed(Expense expense) =>
        expense.Event is { IsClosed: true };
}
