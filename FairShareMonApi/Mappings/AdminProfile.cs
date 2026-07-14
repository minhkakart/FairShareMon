using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Repositories.Admin;

namespace FairShareMonApi.Mappings;

/// <summary>
/// Maps the M11 admin aggregates/entities to their response DTOs. All source data is account metadata
/// or tier-grant records only - never ledger data (R10). Money stays <c>decimal</c>. The listing's
/// grant-count/last-grant fields are stitched in the service, so they are ignored here.
/// </summary>
public class AdminProfile : Profile
{
    public AdminProfile()
    {
        CreateMap<AdminUserAccount, AdminUserRow>()
            .ForMember(dest => dest.GrantCount, opt => opt.Ignore())
            .ForMember(dest => dest.LastGrantAt, opt => opt.Ignore());

        // Detail maps directly from the User entity (account metadata only - PasswordHash etc. are not
        // mapped); the grant history is stitched by the service.
        CreateMap<User, AdminUserDetailResponse>()
            .ForMember(dest => dest.Grants, opt => opt.Ignore());

        CreateMap<TierGrant, TierGrantRow>();

        CreateMap<CountByKey, MetricCount>();
        CreateMap<PeriodCount, PeriodMetric>();
        CreateMap<AdminMetricsAggregate, AdminMetricsResponse>()
            .ForMember(dest => dest.From, opt => opt.Ignore())
            .ForMember(dest => dest.To, opt => opt.Ignore());

        CreateMap<RevenueBucket, RevenueBucketRow>();
        CreateMap<RevenueAggregate, RevenueResponse>()
            .ForMember(dest => dest.From, opt => opt.Ignore())
            .ForMember(dest => dest.To, opt => opt.Ignore())
            .ForMember(dest => dest.Bucket, opt => opt.Ignore());
    }
}
