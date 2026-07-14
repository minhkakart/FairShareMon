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
        CreateMap<Expense, ExpenseResponse>()
            .ForMember(dest => dest.Total, opt => opt.MapFrom(src => src.Shares.Sum(share => share.Amount)))
            .ForMember(dest => dest.Payer, opt => opt.MapFrom(src => src.PayerMember))
            .ForMember(dest => dest.Category, opt => opt.MapFrom(src => src.Category))
            .ForMember(dest => dest.Shares, opt => opt.MapFrom(src => src.Shares))
            .ForMember(dest => dest.Tags, opt => opt.MapFrom(src => src.ExpenseTags.Select(link => link.Tag)));

        CreateMap<Expense, ExpenseSummaryResponse>()
            .ForMember(dest => dest.Total, opt => opt.MapFrom(src => src.Shares.Sum(share => share.Amount)))
            .ForMember(dest => dest.Payer, opt => opt.MapFrom(src => src.PayerMember))
            .ForMember(dest => dest.Category, opt => opt.MapFrom(src => src.Category))
            .ForMember(dest => dest.TagNames, opt => opt.MapFrom(src => src.ExpenseTags.Select(link => link.Tag.Name)))
            .ForMember(dest => dest.ShareCount, opt => opt.MapFrom(src => src.Shares.Count));
    }
}
