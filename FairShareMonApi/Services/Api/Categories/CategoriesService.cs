using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Categories;

/// <summary>
/// Business logic for The-ideal.md §3.3 / §4.6 (Danh mục chi tiêu): list / get / create / update /
/// soft-delete categories and reassign the default, all resource-owned (an ownership miss -&gt;
/// <c>CategoryNotFound</c> 404, never 403). Enforces the default-category invariant (exactly one,
/// not deletable, atomic swap), unique active name per ledger, and reactivation-on-name-reuse (OQ4).
/// Also owns the idempotent suggested-category backfill for pre-existing users.
/// </summary>
public interface ICategoriesService
{
    Task<IReadOnlyList<CategoryResponse>> ListAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default);

    Task<CategoryResponse> GetAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default);

    Task<CategoryResponse> CreateAsync(string userUuid, CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<CategoryResponse> UpdateAsync(string userUuid, string categoryUuid, UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    Task SetDefaultAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default);

    /// <summary>Idempotent backfill: gives every user lacking an active default category the suggested set (or elects a default). Returns how many users were fixed.</summary>
    Task<int> EnsureSuggestedCategoriesForAllAsync(CancellationToken cancellationToken = default);
}

[ScopedService(typeof(ICategoriesService))]
public sealed class CategoriesService(
    ICategoryRepository categoryRepository,
    IMapper mapper,
    IValidator<CreateCategoryRequest> createValidator,
    IValidator<UpdateCategoryRequest> updateValidator) : ICategoriesService
{
    public async Task<IReadOnlyList<CategoryResponse>> ListAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var categories = await categoryRepository.ListByUserAsync(userUuid, includeDeleted, cancellationToken);
        return mapper.Map<IReadOnlyList<CategoryResponse>>(categories);
    }

    public async Task<CategoryResponse> GetAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetByUuidAsync(userUuid, categoryUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<CategoryResponse>(category);
    }

    public async Task<CategoryResponse> CreateAsync(string userUuid, CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        // The atomic create-path (active-dup -> duplicate; soft-deleted match -> reactivate; else
        // insert) runs inside one repository transaction to avoid a check-then-act race (OQ4/OQ5).
        var result = await categoryRepository.CreateAsync(userUuid, request.Name.Trim(), request.Color, request.Icon?.Trim(), cancellationToken);
        return result.Status switch
        {
            NameWriteStatus.NameDuplicate => throw NameDuplicate(),
            NameWriteStatus.NotFound => throw NotFound(),
            _ => mapper.Map<CategoryResponse>(result.Entity)
        };
    }

    public async Task<CategoryResponse> UpdateAsync(string userUuid, string categoryUuid, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var result = await categoryRepository.UpdateAsync(userUuid, categoryUuid, request.Name.Trim(), request.Color, request.Icon?.Trim(), cancellationToken);
        return result.Status switch
        {
            NameWriteStatus.NameDuplicate => throw NameDuplicate(),
            NameWriteStatus.NotFound => throw NotFound(),
            _ => mapper.Map<CategoryResponse>(result.Entity)
        };
    }

    public async Task SetDefaultAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default)
    {
        // Only an active, owned category can be made default; a soft-deleted/foreign one -> 404.
        var updated = await categoryRepository.SetDefaultAsync(userUuid, categoryUuid, cancellationToken);
        if (!updated)
            throw NotFound();
    }

    public async Task DeleteAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetByUuidAsync(userUuid, categoryUuid, cancellationToken)
            ?? throw NotFound();

        if (category.IsDefault)
            throw new ErrorException(ErrorCodes.DefaultCategoryNotDeletable, "Không thể xóa danh mục mặc định.");

        await categoryRepository.SoftDeleteAsync(userUuid, categoryUuid, cancellationToken);
    }

    public async Task<int> EnsureSuggestedCategoriesForAllAsync(CancellationToken cancellationToken = default)
    {
        var userUuids = await categoryRepository.GetUserUuidsWithoutDefaultCategoryAsync(cancellationToken);

        var fixedCount = 0;
        foreach (var userUuid in userUuids)
        {
            if (await categoryRepository.SeedSuggestedOrElectDefaultAsync(userUuid, cancellationToken))
                fixedCount++;
        }

        return fixedCount;
    }

    private static ErrorException NotFound() =>
        new(ErrorCodes.CategoryNotFound, "Không tìm thấy danh mục.");

    private static ErrorException NameDuplicate() =>
        new(ErrorCodes.CategoryNameDuplicate, "Tên danh mục đã tồn tại.");
}
