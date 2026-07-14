using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;

namespace FairShareMonApi.Mappings;

public class ExpenseProfile : Profile
{
    public ExpenseProfile()
    {
        // Total is derived from the shares (OQ1); payer/category/tags display denormalized info incl.
        // their isDeleted flag (§4.7). Category/Member/Tag responses compose via their own profiles.
        // Event membership (M6, OQ14): nullable eventUuid/eventName/eventIsClosed from the Event nav.
        CreateMap<Expense, ExpenseResponse>()
            .ForMember(dest => dest.Total, opt => opt.MapFrom(src => src.Shares.Sum(share => share.Amount)))
            .ForMember(dest => dest.Payer, opt => opt.MapFrom(src => src.PayerMember))
            .ForMember(dest => dest.Category, opt => opt.MapFrom(src => src.Category))
            .ForMember(dest => dest.Shares, opt => opt.MapFrom(src => src.Shares))
            .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.ExpenseTags.Select(link => link.Tag)))
            .ForMember(dest => dest.EventUuid, opt => opt.MapFrom(src => src.Event != null ? src.Event.Uuid : null))
            .ForMember(dest => dest.EventName, opt => opt.MapFrom(src => src.Event != null ? src.Event.Name : null))
            .ForMember(dest => dest.EventIsClosed, opt => opt.MapFrom(src => src.Event != null ? (bool?)src.Event.IsClosed : null));

        CreateMap<Expense, ExpenseSummaryResponse>()
            .ForMember(dest => dest.Total, opt => opt.MapFrom(src => src.Shares.Sum(share => share.Amount)))
            .ForMember(dest => dest.Payer, opt => opt.MapFrom(src => src.PayerMember))
            .ForMember(dest => dest.Category, opt => opt.MapFrom(src => src.Category))
            .ForMember(dest => dest.TagNames, opt => opt.MapFrom(src => src.ExpenseTags.Select(link => link.Tag.Name)))
            .ForMember(dest => dest.ShareCount, opt => opt.MapFrom(src => src.Shares.Count))
            .ForMember(dest => dest.EventUuid, opt => opt.MapFrom(src => src.Event != null ? src.Event.Uuid : null))
            .ForMember(dest => dest.EventName, opt => opt.MapFrom(src => src.Event != null ? src.Event.Name : null))
            .ForMember(dest => dest.EventIsClosed, opt => opt.MapFrom(src => src.Event != null ? (bool?)src.Event.IsClosed : null));
    }
}
