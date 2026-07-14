using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Tags;

namespace FairShareMonApi.Mappings;

public class TagProfile : Profile
{
    public TagProfile()
    {
        CreateMap<Tag, TagResponse>();
    }
}
