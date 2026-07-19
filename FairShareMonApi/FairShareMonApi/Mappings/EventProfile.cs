using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Events;

namespace FairShareMonApi.Mappings;

public class EventProfile : Profile
{
    public EventProfile()
    {
        // expenseCount, totalAdvanced and the effective updatedAt are all derived in-memory from the
        // loaded Expenses (+ .Shares) graph (OQ9/OQ15) - the repository Includes Expenses.Shares on the
        // list/detail reads.
        //  - totalAdvanced = Σ Share.Amount over every share of every expense in the event (The-ideal §3.7);
        //    0 when the event has no expenses/shares.
        //  - updatedAt = effective last-activity timestamp = max of the event's own UpdatedAt and every
        //    child expense/share UpdatedAt (RESOLVED Open Question, Option B). It no longer maps by name
        //    convention, so it needs an explicit ForMember. event.UpdatedAt is always in the set, so the
        //    empty-children case falls back to it naturally.
        CreateMap<Event, EventResponse>()
            .ForMember(dest => dest.ExpenseCount, opt => opt.MapFrom(src => src.Expenses.Count))
            .ForMember(dest => dest.TotalAdvanced,
                opt => opt.MapFrom(src => src.Expenses.SelectMany(e => e.Shares).Sum(s => s.Amount)))
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src =>
                new[] { src.UpdatedAt }
                    .Concat(src.Expenses.Select(e => e.UpdatedAt))
                    .Concat(src.Expenses.SelectMany(e => e.Shares).Select(s => s.UpdatedAt))
                    .Max()));

        CreateMap<Event, EventSummaryResponse>()
            .ForMember(dest => dest.ExpenseCount, opt => opt.MapFrom(src => src.Expenses.Count))
            .ForMember(dest => dest.TotalAdvanced,
                opt => opt.MapFrom(src => src.Expenses.SelectMany(e => e.Shares).Sum(s => s.Amount)))
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src =>
                new[] { src.UpdatedAt }
                    .Concat(src.Expenses.Select(e => e.UpdatedAt))
                    .Concat(src.Expenses.SelectMany(e => e.Shares).Select(s => s.UpdatedAt))
                    .Max()));
    }
}
