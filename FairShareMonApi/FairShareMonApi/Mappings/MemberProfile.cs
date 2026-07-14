using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Members;

namespace FairShareMonApi.Mappings;

public class MemberProfile : Profile
{
    public MemberProfile()
    {
        CreateMap<Member, MemberResponse>();
    }
}
