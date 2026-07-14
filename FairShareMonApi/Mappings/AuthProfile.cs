using AutoMapper;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Auth;

namespace FairShareMonApi.Mappings;

public class AuthProfile : Profile
{
    public AuthProfile()
    {
        CreateMap<User, UserResponse>();
        CreateMap<TokenPair, TokenPairResponse>();
    }
}
