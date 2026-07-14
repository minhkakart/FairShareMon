using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Categories;
using FairShareMonApi.Validators.Categories;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>CategoriesService</c> over a fake <see cref="ICategoryRepository"/> (that
/// re-implements the atomic create-path decision tree in memory) plus the real AutoMapper profile and
/// real validators (no DB). Proves: create trims the name; the create-path tree (active-dup → 4001,
/// soft-deleted-name → reactivate overwriting color/icon while leaving the default flag untouched,
/// else insert); update dup → 4001 and miss → 4000; delete-of-default → 4002 WITHOUT calling
/// soft-delete; delete non-default soft-deletes; set-default miss/deleted → 4000; <c>includeDeleted</c>
/// passthrough; and the idempotent backfill.
/// </summary>
public class CategoriesServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000cd01";
    private const string Color = "#F97316";

    private readonly FakeCategoryRepository _repository = new();
    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<CategoryProfile>()).CreateMapper();

    private CategoriesService CreateService() =>
        new(_repository, _mapper, new CreateCategoryRequestValidator(), new UpdateCategoryRequestValidator());

    private Category AddCategory(string name, string color = Color, string? icon = null, bool isDefault = false, bool deleted = false)
    {
        var category = new Category { Name = name, Color = color, Icon = icon, IsDefault = isDefault, IsDeleted = deleted };
        _repository.Categories.Add((UserUuid, category));
        return category;
    }

    private static CreateCategoryRequest CreateRequest(string name, string color = Color, string? icon = null) =>
        new() { Name = name, Color = color, Icon = icon };

    [Fact]
    public async Task CreateAsync_ValidRequest_InsertsActiveNonDefaultCategory()
    {
        var response = await CreateService().CreateAsync(UserUuid, CreateRequest("Ăn uống", icon: "🍜"));

        Assert.Equal("Ăn uống", response.Name);
        Assert.Equal(Color, response.Color);
        Assert.Equal("🍜", response.Icon);
        Assert.False(response.IsDefault); // an API-created category is never the default
        Assert.False(response.IsDeleted);
        Assert.Single(_repository.Categories);
    }

    [Fact]
    public async Task CreateAsync_NameWithSurroundingWhitespace_IsTrimmed()
    {
        var response = await CreateService().CreateAsync(UserUuid, CreateRequest("   Đi lại   "));

        Assert.Equal("Đi lại", response.Name);
    }

    [Fact]
    public async Task CreateAsync_ActiveNameCollision_ThrowsCategoryNameDuplicate4001()
    {
        AddCategory("Ăn uống");

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, CreateRequest("Ăn uống")));

        Assert.Equal(ErrorCodes.CategoryNameDuplicate, exception.Code);
    }

    [Fact]
    public async Task CreateAsync_SoftDeletedNameMatch_ReactivatesAndOverwritesColorIconLeavesDefaultUntouched()
    {
        var deleted = AddCategory("Ăn uống", color: "#111111", icon: "old", deleted: true);

        var response = await CreateService().CreateAsync(UserUuid, CreateRequest("Ăn uống", color: "#F97316", icon: "🍜"));

        Assert.Equal(deleted.Uuid, response.Uuid); // same row revived, not a duplicate
        Assert.False(response.IsDeleted);
        Assert.Equal("#F97316", response.Color); // OQ5: color/icon overwritten with request values
        Assert.Equal("🍜", response.Icon);
        Assert.False(deleted.IsDefault); // default flag untouched by reactivation
        Assert.Single(_repository.Categories); // no second row created
    }

    [Fact]
    public async Task CreateAsync_InvalidColor_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().CreateAsync(UserUuid, CreateRequest("Ăn uống", color: "not-a-color")));
    }

    [Fact]
    public async Task CreateAsync_UnknownUser_ThrowsCategoryNotFound()
    {
        _repository.FailCreateWithUnknownUser = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, CreateRequest("Ăn uống")));

        Assert.Equal(ErrorCodes.CategoryNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Miss_ThrowsCategoryNotFound4000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetAsync(UserUuid, "no-such-category"));

        Assert.Equal(ErrorCodes.CategoryNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Found_ReturnsResponse()
    {
        var category = AddCategory("Ăn uống", icon: "🍜", isDefault: true);

        var response = await CreateService().GetAsync(UserUuid, category.Uuid);

        Assert.Equal(category.Uuid, response.Uuid);
        Assert.True(response.IsDefault);
    }

    [Fact]
    public async Task UpdateAsync_Miss_ThrowsCategoryNotFound4000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "no-such-category", new UpdateCategoryRequest { Name = "X", Color = Color }));

        Assert.Equal(ErrorCodes.CategoryNotFound, exception.Code);
    }

    [Fact]
    public async Task UpdateAsync_ActiveNameCollisionWithAnother_ThrowsCategoryNameDuplicate4001()
    {
        AddCategory("Ăn uống");
        var target = AddCategory("Đi lại");

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, target.Uuid, new UpdateCategoryRequest { Name = "Ăn uống", Color = Color }));

        Assert.Equal(ErrorCodes.CategoryNameDuplicate, exception.Code);
    }

    [Fact]
    public async Task UpdateAsync_ValidChange_PersistsNameColorIcon()
    {
        var target = AddCategory("Đi lại", color: "#111111", icon: "old");

        var response = await CreateService().UpdateAsync(UserUuid, target.Uuid,
            new UpdateCategoryRequest { Name = "  Di chuyển  ", Color = "#3B82F6", Icon = "🚗" });

        Assert.Equal("Di chuyển", response.Name); // trimmed
        Assert.Equal("#3B82F6", response.Color);
        Assert.Equal("🚗", response.Icon);
    }

    [Fact]
    public async Task DeleteAsync_DefaultCategory_Throws4002WithoutCallingSoftDelete()
    {
        var defaultCategory = AddCategory("Ăn uống", isDefault: true);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DeleteAsync(UserUuid, defaultCategory.Uuid));

        Assert.Equal(ErrorCodes.DefaultCategoryNotDeletable, exception.Code);
        Assert.Empty(_repository.SoftDeletedUuids); // guard fires before any soft-delete
        Assert.False(defaultCategory.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_Miss_ThrowsCategoryNotFound4000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DeleteAsync(UserUuid, "no-such-category"));

        Assert.Equal(ErrorCodes.CategoryNotFound, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_NonDefaultCategory_SoftDeletes()
    {
        var category = AddCategory("Đi lại");

        await CreateService().DeleteAsync(UserUuid, category.Uuid);

        Assert.Contains(category.Uuid, _repository.SoftDeletedUuids);
    }

    [Fact]
    public async Task SetDefaultAsync_Miss_ThrowsCategoryNotFound4000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().SetDefaultAsync(UserUuid, "no-such-category"));

        Assert.Equal(ErrorCodes.CategoryNotFound, exception.Code);
    }

    [Fact]
    public async Task SetDefaultAsync_ActiveOwnedCategory_Succeeds()
    {
        var category = AddCategory("Đi lại");

        await CreateService().SetDefaultAsync(UserUuid, category.Uuid);

        Assert.True(category.IsDefault);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_PassesIncludeDeletedThrough(bool includeDeleted)
    {
        AddCategory("Ăn uống");

        await CreateService().ListAsync(UserUuid, includeDeleted);

        Assert.Equal(includeDeleted, _repository.LastListIncludeDeleted);
    }

    [Fact]
    public async Task EnsureSuggestedCategoriesForAllAsync_SeedsEachMissingUserAndReturnsCount()
    {
        _repository.UsersWithoutDefault.AddRange(["user-a", "user-b"]);

        var created = await CreateService().EnsureSuggestedCategoriesForAllAsync();

        Assert.Equal(2, created);
        Assert.Equal(["user-a", "user-b"], _repository.SeededUserUuids);
    }

    [Fact]
    public async Task EnsureSuggestedCategoriesForAllAsync_NoMissingUsers_CreatesNothing()
    {
        var created = await CreateService().EnsureSuggestedCategoriesForAllAsync();

        Assert.Equal(0, created);
        Assert.Empty(_repository.SeededUserUuids);
    }

    /// <summary>
    /// In-memory stand-in for the categories table. Mirrors the repository's atomic create-path
    /// decision tree (active-dup → duplicate; soft-deleted match → reactivate; else insert) and
    /// update uniqueness re-check so the service's mapping/trim/guard behavior can be proven without a DB.
    /// </summary>
    private sealed class FakeCategoryRepository : ICategoryRepository
    {
        public List<(string UserUuid, Category Category)> Categories { get; } = [];

        public List<string> SoftDeletedUuids { get; } = [];

        public List<string> UsersWithoutDefault { get; } = [];

        public List<string> SeededUserUuids { get; } = [];

        public bool? LastListIncludeDeleted { get; private set; }

        public bool FailCreateWithUnknownUser { get; set; }

        public Task<IReadOnlyList<Category>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default)
        {
            LastListIncludeDeleted = includeDeleted;
            var categories = Categories
                .Where(entry => entry.UserUuid == userUuid && (includeDeleted || !entry.Category.IsDeleted))
                .Select(entry => entry.Category)
                .ToList();
            return Task.FromResult<IReadOnlyList<Category>>(categories);
        }

        public Task<Category?> GetByUuidAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Categories
                .Where(entry => entry.UserUuid == userUuid && entry.Category.Uuid == categoryUuid)
                .Select(entry => entry.Category)
                .FirstOrDefault());

        public Task<Category?> FindActiveByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Categories
                .Where(entry => entry.UserUuid == userUuid && !entry.Category.IsDeleted && entry.Category.Name == name)
                .Select(entry => entry.Category)
                .FirstOrDefault());

        public Task<Category?> FindDeletedByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Categories
                .Where(entry => entry.UserUuid == userUuid && entry.Category.IsDeleted && entry.Category.Name == name)
                .Select(entry => entry.Category)
                .FirstOrDefault());

        public Task<NameWriteResult<Category>> CreateAsync(string userUuid, string name, string color, string? icon, CancellationToken cancellationToken = default)
        {
            if (FailCreateWithUnknownUser)
                return Task.FromResult(NameWriteResult<Category>.NotFound());

            var active = Categories
                .Where(entry => entry.UserUuid == userUuid && !entry.Category.IsDeleted && entry.Category.Name == name)
                .Select(entry => entry.Category)
                .FirstOrDefault();
            if (active is not null)
                return Task.FromResult(NameWriteResult<Category>.NameDuplicate());

            var deleted = Categories
                .Where(entry => entry.UserUuid == userUuid && entry.Category.IsDeleted && entry.Category.Name == name)
                .Select(entry => entry.Category)
                .FirstOrDefault();
            if (deleted is not null)
            {
                deleted.IsDeleted = false;
                deleted.Color = color;
                deleted.Icon = icon; // default flag intentionally left untouched (OQ5)
                return Task.FromResult(NameWriteResult<Category>.Reactivated(deleted));
            }

            var category = new Category { Name = name, Color = color, Icon = icon };
            Categories.Add((userUuid, category));
            return Task.FromResult(NameWriteResult<Category>.Created(category));
        }

        public Task<NameWriteResult<Category>> UpdateAsync(string userUuid, string categoryUuid, string name, string color, string? icon, CancellationToken cancellationToken = default)
        {
            var category = Categories
                .Where(entry => entry.UserUuid == userUuid && !entry.Category.IsDeleted && entry.Category.Uuid == categoryUuid)
                .Select(entry => entry.Category)
                .FirstOrDefault();
            if (category is null)
                return Task.FromResult(NameWriteResult<Category>.NotFound());

            var duplicate = Categories.Any(entry => entry.UserUuid == userUuid
                && !entry.Category.IsDeleted
                && entry.Category.Uuid != categoryUuid
                && entry.Category.Name == name);
            if (duplicate)
                return Task.FromResult(NameWriteResult<Category>.NameDuplicate());

            category.Name = name;
            category.Color = color;
            category.Icon = icon;
            return Task.FromResult(NameWriteResult<Category>.Updated(category));
        }

        public Task<bool> SoftDeleteAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default)
        {
            var category = Categories
                .Where(entry => entry.UserUuid == userUuid && entry.Category.Uuid == categoryUuid)
                .Select(entry => entry.Category)
                .FirstOrDefault();
            if (category is null)
                return Task.FromResult(false);

            category.IsDeleted = true;
            SoftDeletedUuids.Add(categoryUuid);
            return Task.FromResult(true);
        }

        public Task<bool> SetDefaultAsync(string userUuid, string categoryUuid, CancellationToken cancellationToken = default)
        {
            var target = Categories
                .Where(entry => entry.UserUuid == userUuid && !entry.Category.IsDeleted && entry.Category.Uuid == categoryUuid)
                .Select(entry => entry.Category)
                .FirstOrDefault();
            if (target is null)
                return Task.FromResult(false);

            foreach (var entry in Categories.Where(entry => entry.UserUuid == userUuid))
                entry.Category.IsDefault = false;
            target.IsDefault = true;
            return Task.FromResult(true);
        }

        public Task<bool> HasAnyCategoryAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Categories.Any(entry => entry.UserUuid == userUuid && !entry.Category.IsDeleted));

        public Task<IReadOnlyList<string>> GetUserUuidsWithoutDefaultCategoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(UsersWithoutDefault.ToList());

        public Task<bool> SeedSuggestedOrElectDefaultAsync(string userUuid, CancellationToken cancellationToken = default)
        {
            SeededUserUuids.Add(userUuid);
            return Task.FromResult(true);
        }

        public IQueryable<Category> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(
            Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(
            Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
