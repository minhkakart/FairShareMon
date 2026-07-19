using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Events;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for the AutoMapper <see cref="EventProfile"/> - the derived fields added by
/// planning/event-summary-advanced-and-updated.md (Option B). Proves, for BOTH
/// <c>Event -> EventSummaryResponse</c> and <c>Event -> EventResponse</c>:
///  - <c>TotalAdvanced</c> = Σ Share.Amount over every share of every expense in the event (multiple
///    expenses × multiple shares), 0 when there are no expenses and 0 when every share is 0đ; and
///  - <c>UpdatedAt</c> = effective last-activity = max over { event.UpdatedAt } ∪ every expense.UpdatedAt
///    ∪ every share.UpdatedAt - covering the case where a child timestamp is later than the event's, the
///    case where the event's own timestamp is the latest, and the empty-children fallback to
///    event.UpdatedAt.
/// The pure objects here need no member-uniqueness (that DB constraint is exercised by the integration
/// tests), so an expense may carry several same-shaped shares for arithmetic clarity.
/// </summary>
public class EventProfileTests
{
    private readonly IMapper _mapper =
        new MapperConfiguration(config => config.AddProfile<EventProfile>()).CreateMapper();

    private static readonly DateTime T1 = new(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T3 = new(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T4 = new(2026, 7, 17, 11, 0, 0, DateTimeKind.Utc);

    private static Share ShareOf(decimal amount, DateTime updatedAt) =>
        new() { Amount = amount, UpdatedAt = updatedAt };

    private static Expense ExpenseOf(DateTime updatedAt, params Share[] shares)
    {
        var expense = new Expense { Name = "phiếu", ExpenseTime = T1, UpdatedAt = updatedAt };
        foreach (var share in shares)
            expense.Shares.Add(share);
        return expense;
    }

    /// <summary>Event with two expenses (800 + 100) → TotalAdvanced == 900 on the summary map.</summary>
    [Fact]
    public void Map_EventToSummary_TotalAdvanced_SumsAllSharesAcrossAllExpenses()
    {
        var evt = new Event { Name = "Đà Lạt", UpdatedAt = T1 };
        evt.Expenses.Add(ExpenseOf(T1, ShareOf(200m, T1), ShareOf(200m, T1), ShareOf(200m, T1), ShareOf(200m, T1))); // 800
        evt.Expenses.Add(ExpenseOf(T1, ShareOf(50m, T1), ShareOf(50m, T1))); // 100

        var summary = _mapper.Map<EventSummaryResponse>(evt);

        Assert.Equal(900m, summary.TotalAdvanced);
        Assert.Equal(2, summary.ExpenseCount); // unchanged existing derivation
    }

    /// <summary>Same total is produced on the detail map.</summary>
    [Fact]
    public void Map_EventToResponse_TotalAdvanced_SumsAllSharesAcrossAllExpenses()
    {
        var evt = new Event { Name = "Đà Lạt", UpdatedAt = T1 };
        evt.Expenses.Add(ExpenseOf(T1, ShareOf(200m, T1), ShareOf(200m, T1), ShareOf(200m, T1), ShareOf(200m, T1)));
        evt.Expenses.Add(ExpenseOf(T1, ShareOf(50m, T1), ShareOf(50m, T1)));

        var response = _mapper.Map<EventResponse>(evt);

        Assert.Equal(900m, response.TotalAdvanced);
        Assert.Equal(2, response.ExpenseCount);
    }

    [Fact]
    public void Map_EventToSummary_TotalAdvanced_IsZero_WhenNoExpenses()
    {
        var evt = new Event { Name = "Rỗng", UpdatedAt = T2 };

        var summary = _mapper.Map<EventSummaryResponse>(evt);

        Assert.Equal(0m, summary.TotalAdvanced);
        Assert.Equal(0, summary.ExpenseCount);
    }

    [Fact]
    public void Map_EventToResponse_TotalAdvanced_IsZero_WhenNoExpenses()
    {
        var evt = new Event { Name = "Rỗng", UpdatedAt = T2 };

        var response = _mapper.Map<EventResponse>(evt);

        Assert.Equal(0m, response.TotalAdvanced);
    }

    /// <summary>An expense whose shares are all 0đ contributes 0 (0đ shares are valid, §4.3).</summary>
    [Fact]
    public void Map_EventToSummary_TotalAdvanced_IsZero_WhenAllSharesZero()
    {
        var evt = new Event { Name = "Toàn 0đ", UpdatedAt = T1 };
        evt.Expenses.Add(ExpenseOf(T1, ShareOf(0m, T1), ShareOf(0m, T1)));

        var summary = _mapper.Map<EventSummaryResponse>(evt);

        Assert.Equal(0m, summary.TotalAdvanced);
        Assert.Equal(1, summary.ExpenseCount); // the expense still counts
    }

    /// <summary>Effective UpdatedAt takes a child (share) timestamp when it is later than the event's own.</summary>
    [Fact]
    public void Map_EventToSummary_UpdatedAt_UsesChildTimestamp_WhenChildLaterThanEvent()
    {
        var evt = new Event { Name = "Đà Lạt", UpdatedAt = T1 };
        evt.Expenses.Add(ExpenseOf(T2, ShareOf(100m, T4), ShareOf(100m, T3))); // latest child = T4

        var summary = _mapper.Map<EventSummaryResponse>(evt);

        Assert.Equal(T4, summary.UpdatedAt);
    }

    /// <summary>Effective UpdatedAt keeps the event's own timestamp when it is the latest of the set.</summary>
    [Fact]
    public void Map_EventToSummary_UpdatedAt_UsesEventTimestamp_WhenEventLatest()
    {
        var evt = new Event { Name = "Đà Lạt", UpdatedAt = T4 }; // event row is the newest
        evt.Expenses.Add(ExpenseOf(T2, ShareOf(100m, T1), ShareOf(100m, T3)));

        var summary = _mapper.Map<EventSummaryResponse>(evt);

        Assert.Equal(T4, summary.UpdatedAt);
    }

    /// <summary>With no expenses/shares the max set is just the event → UpdatedAt == event.UpdatedAt.</summary>
    [Fact]
    public void Map_EventToSummary_UpdatedAt_FallsBackToEventTimestamp_WhenNoChildren()
    {
        var evt = new Event { Name = "Rỗng", UpdatedAt = T2 };

        var summary = _mapper.Map<EventSummaryResponse>(evt);

        Assert.Equal(T2, summary.UpdatedAt);
    }

    /// <summary>Detail map computes the same max over event + expenses + shares.</summary>
    [Fact]
    public void Map_EventToResponse_UpdatedAt_UsesMaxAcrossEventExpensesAndShares()
    {
        var evt = new Event { Name = "Đà Lạt", UpdatedAt = T1 };
        evt.Expenses.Add(ExpenseOf(T3, ShareOf(100m, T2))); // expense T3 is the max here
        evt.Expenses.Add(ExpenseOf(T2, ShareOf(100m, T2)));

        var response = _mapper.Map<EventResponse>(evt);

        Assert.Equal(T3, response.UpdatedAt);
    }

    /// <summary>An expense with UpdatedAt but zero shares still contributes its own timestamp to the max.</summary>
    [Fact]
    public void Map_EventToResponse_UpdatedAt_ConsidersExpenseTimestamp_WhenExpenseHasNoShares()
    {
        var evt = new Event { Name = "Đà Lạt", UpdatedAt = T1 };
        evt.Expenses.Add(ExpenseOf(T3)); // no shares, expense.UpdatedAt = T3

        var response = _mapper.Map<EventResponse>(evt);

        Assert.Equal(T3, response.UpdatedAt);
        Assert.Equal(0m, response.TotalAdvanced);
    }

    /// <summary>Both maps are fully configured (no unmapped destination members).</summary>
    [Fact]
    public void EventProfile_ConfigurationIsValid()
    {
        var configuration = new MapperConfiguration(config => config.AddProfile<EventProfile>());

        configuration.AssertConfigurationIsValid();
    }
}
