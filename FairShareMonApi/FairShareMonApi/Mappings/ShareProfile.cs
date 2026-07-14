using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Shares;

namespace FairShareMonApi.Mappings;

public class ShareProfile : Profile
{
    public ShareProfile()
    {
        // Member -> MemberResponse composes via MemberProfile; a soft-deleted member still displays (§4.7).
        CreateMap<Share, ShareResponse>();
    }
}
