using FairShareMonApi.Database.Entities;
using FairShareMonApi.Utils;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Shared reconciliation between the per-<see cref="Share"/> settled flag (Layer A) and the
/// whole-<see cref="Expense"/> settled flag (settled-per-member OQ3a). Single source for the "billable"
/// predicate so the per-share write path (<c>ShareRepository.SetSettledAsync</c>) and the whole-expense
/// write path (<c>ExpenseRepository.SetSettledAsync</c>) agree.
///
/// A share is <b>billable</b> when it represents something owed to the payer: <c>Amount &gt; 0</c> and
/// <c>member ≠ payer</c>. The payer's own share and any 0đ share (e.g. the owner-rep 0đ share) owe
/// nothing and are treated as settled-by-definition - excluded from the predicate; toggling them stores
/// the flag but has no derived effect (OQ6a).
/// </summary>
public static class SettlementReconciler
{
    /// <summary>A share that represents a real debt to the payer (<c>Amount &gt; 0</c> and member ≠ payer).</summary>
    public static bool IsBillable(Share share, Expense expense) =>
        share.Amount > 0m && share.MemberId != expense.PayerMemberId;

    /// <summary>
    /// Recomputes <see cref="Expense.IsSettled"/> = (all billable shares settled) and
    /// <see cref="Expense.SettledAt"/> (latest billable share settled_at, or now when it flips true with
    /// no timestamped share; cleared when false). An expense with no billable shares reconciles to settled
    /// by definition (OQ6a). The expense's <see cref="Expense.Shares"/> must be loaded.
    /// </summary>
    public static void ReconcileExpense(Expense expense)
    {
        var billable = expense.Shares.Where(share => IsBillable(share, expense)).ToList();
        var allSettled = billable.All(share => share.IsSettled);

        expense.IsSettled = allSettled;
        if (!allSettled)
        {
            expense.SettledAt = null;
            return;
        }

        expense.SettledAt = billable.Max(share => share.SettledAt) ?? AppDateTime.Now;
    }

    /// <summary>
    /// Cascades a whole-expense settled toggle to its billable shares (OQ3a): each billable share's
    /// settled flag + settled_at is set to match, using the shared <paramref name="now"/> so the expense
    /// and its shares carry the same timestamp. Payer-own and 0đ shares are left untouched (OQ6a). The
    /// expense's <see cref="Expense.Shares"/> must be loaded.
    /// </summary>
    public static void CascadeToShares(Expense expense, bool isSettled, DateTime now)
    {
        foreach (var share in expense.Shares.Where(share => IsBillable(share, expense)))
        {
            share.IsSettled = isSettled;
            share.SettledAt = isSettled ? now : null;
        }
    }
}
