using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Events;

namespace FairShareMonApi.Mappings;

public class EventProfile : Profile
{
    public EventProfile()
    {
        // expenseCount is derived from the loaded Expenses collection (OQ9/OQ15) - the repository
        // Includes Expenses on the list/detail reads.
        CreateMap<Event, EventResponse>()
            .ForMember(dest => dest.ExpenseCount, opt => opt.MapFrom(src => src.Expenses.Count));

        CreateMap<Event, EventSummaryResponse>()
            .ForMember(dest => dest.ExpenseCount, opt => opt.MapFrom(src => src.Expenses.Count));
    }
}
