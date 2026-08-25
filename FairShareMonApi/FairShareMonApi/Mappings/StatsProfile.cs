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
            .ForMember(dest => dest.Balance, opt => opt.MapFrom(src => src.Advanced - src.Owed))
            // ClearedAmount maps through by convention (identically-named property) - the sole source of
            // truth Outstanding/SettlementStatus are computed FROM (event-expense-settlement-sync Step M2.5).
            // Outstanding/SettlementStatus themselves are the derived Layer B overlay - computed once in
            // StatsService, not here (D2 / OQ8a).
            .ForMember(dest => dest.Outstanding, opt => opt.Ignore())
            .ForMember(dest => dest.SettlementStatus, opt => opt.Ignore());

        CreateMap<CategoryStatAggregate, CategoryStatRow>();

        CreateMap<OverviewAggregate, OverviewStatsResponse>();
    }
}
