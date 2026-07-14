using System.Text.Json;
using AutoMapper;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;

namespace FairShareMonApi.Mappings;

public class AuditLogProfile : Profile
{
    public AuditLogProfile()
    {
        CreateMap<AuditLog, AuditLogResponse>()
            .ForMember(dest => dest.EntityType, opt => opt.MapFrom(src => src.EntityType.ToString()))
            .ForMember(dest => dest.Action, opt => opt.MapFrom(src => src.Action.ToString()))
            .ForMember(dest => dest.Before, opt => opt.MapFrom(src => Deserialize(src.BeforeData)))
            .ForMember(dest => dest.After, opt => opt.MapFrom(src => Deserialize(src.AfterData)));
    }

    /// <summary>Turns a stored JSON snapshot back into a structured object for the response (null-safe).</summary>
    private static object? Deserialize(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<JsonElement>(json);
}
