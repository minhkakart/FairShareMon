using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Wallet;

namespace FairShareMonApi.Mappings;

public class BankAccountProfile : Profile
{
    public BankAccountProfile()
    {
        CreateMap<BankAccount, BankAccountResponse>();
    }
}
