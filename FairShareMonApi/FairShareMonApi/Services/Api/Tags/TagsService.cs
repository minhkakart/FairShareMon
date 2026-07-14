using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Tags;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Tags;

/// <summary>
/// Business logic for The-ideal.md §3.4 / §5 (Nhãn): list / get / create / rename / soft-delete
/// tags, all resource-owned (an ownership miss -&gt; <c>TagNotFound</c> 404, never 403). Enforces
/// unique active name per ledger and reactivation-on-name-reuse (reusing a soft-deleted tag's name
/// revives the old row, relinking history, instead of duplicating).
/// </summary>
public interface ITagsService
{
    Task<IReadOnlyList<TagResponse>> ListAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default);

    Task<TagResponse> GetAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default);

    Task<TagResponse> CreateAsync(string userUuid, CreateTagRequest request, CancellationToken cancellationToken = default);

    Task<TagResponse> RenameAsync(string userUuid, string tagUuid, UpdateTagRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(ITagsService))]
public sealed class TagsService(
    ITagRepository tagRepository,
    IMapper mapper,
    IValidator<CreateTagRequest> createValidator,
    IValidator<UpdateTagRequest> updateValidator) : ITagsService
{
    public async Task<IReadOnlyList<TagResponse>> ListAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var tags = await tagRepository.ListByUserAsync(userUuid, includeDeleted, cancellationToken);
        return mapper.Map<IReadOnlyList<TagResponse>>(tags);
    }

    public async Task<TagResponse> GetAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default)
    {
        var tag = await tagRepository.GetByUuidAsync(userUuid, tagUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<TagResponse>(tag);
    }

    public async Task<TagResponse> CreateAsync(string userUuid, CreateTagRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        // Atomic create-path (active-dup -> duplicate; soft-deleted match -> reactivate; else insert)
        // inside one repository transaction to avoid a check-then-act race.
        var result = await tagRepository.CreateAsync(userUuid, request.Name.Trim(), cancellationToken);
        return result.Status switch
        {
            NameWriteStatus.NameDuplicate => throw NameDuplicate(),
            NameWriteStatus.NotFound => throw NotFound(),
            _ => mapper.Map<TagResponse>(result.Entity)
        };
    }

    public async Task<TagResponse> RenameAsync(string userUuid, string tagUuid, UpdateTagRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var result = await tagRepository.RenameAsync(userUuid, tagUuid, request.Name.Trim(), cancellationToken);
        return result.Status switch
        {
            NameWriteStatus.NameDuplicate => throw NameDuplicate(),
            NameWriteStatus.NotFound => throw NotFound(),
            _ => mapper.Map<TagResponse>(result.Entity)
        };
    }

    public async Task DeleteAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default)
    {
        var deleted = await tagRepository.SoftDeleteAsync(userUuid, tagUuid, cancellationToken);
        if (!deleted)
            throw NotFound();
    }

    private static ErrorException NotFound() =>
        new(ErrorCodes.TagNotFound, "Không tìm thấy nhãn.");

    private static ErrorException NameDuplicate() =>
        new(ErrorCodes.TagNameDuplicate, "Tên nhãn đã tồn tại.");
}
