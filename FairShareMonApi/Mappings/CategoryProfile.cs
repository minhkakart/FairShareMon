using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Categories;

namespace FairShareMonApi.Mappings;

public class CategoryProfile : Profile
{
    public CategoryProfile()
    {
        CreateMap<Category, CategoryResponse>();
    }
}
