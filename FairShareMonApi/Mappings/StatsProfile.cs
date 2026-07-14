using AutoMapper;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Repositories.Stats;

namespace FairShareMonApi.Mappings;

/// <summary>
/// Maps the read-only Stats aggregate records to their response DTOs (M7). Money stays <c>decimal</c>;
/// member/category display fields are already denormalized on the aggregate so soft-deleted rows still
/// render (§4.7). The balance (advanced - owed) is computed here in one place (OQ1/OQ14).
/// </summary>
public class StatsProfile : Profile
{
    public StatsProfile()
    {
        CreateMap<MemberBalanceAggregate, MemberBalanceRow>()
            .ForMember(dest => dest.Balance, opt => opt.MapFrom(src => src.Advanced - src.Owed));

        CreateMap<CategoryStatAggregate, CategoryStatRow>();

        CreateMap<OverviewAggregate, OverviewStatsResponse>();
    }
}
