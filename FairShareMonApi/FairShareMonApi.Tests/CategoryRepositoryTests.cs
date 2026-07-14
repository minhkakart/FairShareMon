using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>CategoryRepository</c> against the real MariaDB (skippable). Covers entity
/// defaults (uuid/UTC/user_id/flags), resource-owned scoping (another user's category invisible on
/// get/list/update/delete/set-default), soft-delete-preserves-the-row, the "unique active name"
/// rule + accent/case-insensitive collision (OQ5), reactivation-on-name-reuse (OQ4, overwrites
/// color/icon, default untouched), the default-category invariant (atomic swap), the OQ11 sort, and
/// the backfill support query.
/// </summary>
[Collection("AuthIntegration")]
public class CategoryRepositoryTests(DatabaseFixture fixture) : AuthDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private const string Orange = "#F97316";
    private const string Blue = "#3B82F6";

    private CategoryRepository CreateRepository() => new(CreateContext());

    private async Task<Category> SeedCategoryAsync(ulong userId, string name, string color = Orange, string? icon = null, bool isDefault = false, bool deleted = false)
    {
        await using var context = CreateContext();
        var category = new Category { UserId = userId, Name = name, Color = color, Icon = icon, IsDefault = isDefault, IsDeleted = deleted };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    private async Task<Category> ReloadAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.Categories.AsNoTracking().SingleAsync(category => category.Uuid == uuid);
    }

    [SkippableFact]
    public async Task CreateAsync_NewCategory_PersistsWithUuidUtcUserIdAndActiveNonDefaultFlags()
    {
        var user = await SeedUserAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await CreateRepository().CreateAsync(user.Uuid, "Ăn uống", Orange, "🍜");
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(NameWriteStatus.Created, result.Status);
        var created = result.Entity!;
        Assert.Equal(36, created.Uuid.Length);
        Assert.InRange(created.CreatedAt, before, after); // AppDateTime.Now = UTC
        Assert.False(created.IsDefault);
        Assert.False(created.IsDeleted);

        var persisted = await ReloadAsync(created.Uuid);
        Assert.Equal(user.Id, persisted.UserId);
        Assert.Equal(Orange, persisted.Color);
        Assert.Equal("🍜", persisted.Icon);
    }

    [SkippableFact]
    public async Task CreateAsync_UnknownUser_ReturnsNotFoundAndPersistsNothing()
    {
        var result = await CreateRepository().CreateAsync("00000000-0000-7000-8000-000000000000", "Ăn uống", Orange, null);

        Assert.Equal(NameWriteStatus.NotFound, result.Status);
        Assert.Null(result.Entity);
    }

    [SkippableFact]
    public async Task CreateAsync_ActiveNameCollision_ReturnsNameDuplicate()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Ăn uống");

        var result = await CreateRepository().CreateAsync(user.Uuid, "Ăn uống", Blue, null);

        Assert.Equal(NameWriteStatus.NameDuplicate, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_AccentAndCaseInsensitiveCollision_ReturnsNameDuplicate()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Ăn uống");

        // OQ5: the utf8mb4_unicode_ci column collation treats "an uong" == "Ăn uống".
        var result = await CreateRepository().CreateAsync(user.Uuid, "an uong", Blue, null);

        Assert.Equal(NameWriteStatus.NameDuplicate, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_SoftDeletedNameReuse_ReactivatesSameRowOverwritesColorIconLeavesDefaultUntouched()
    {
        var user = await SeedUserAsync();
        var deleted = await SeedCategoryAsync(user.Id, "Ăn uống", color: "#111111", icon: "old", deleted: true);

        var result = await CreateRepository().CreateAsync(user.Uuid, "Ăn uống", Orange, "🍜");

        Assert.Equal(NameWriteStatus.Reactivated, result.Status);
        Assert.Equal(deleted.Uuid, result.Entity!.Uuid); // same row revived, not a duplicate

        var persisted = await ReloadAsync(deleted.Uuid);
        Assert.False(persisted.IsDeleted);
        Assert.Equal(Orange, persisted.Color); // overwritten with the request values (OQ5)
        Assert.Equal("🍜", persisted.Icon);
        Assert.False(persisted.IsDefault); // default flag untouched by reactivation

        // Exactly one row for that name - no duplicate created.
        await using var context = CreateContext();
        Assert.Equal(1, await context.Categories.CountAsync(category => category.UserId == user.Id));
    }

    [SkippableFact]
    public async Task GetByUuidAsync_AnotherUsersCategory_ReturnsNull()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var category = await SeedCategoryAsync(owner.Id, "Ăn uống");

        var seenByStranger = await CreateRepository().GetByUuidAsync(stranger.Uuid, category.Uuid);
        var seenByOwner = await CreateRepository().GetByUuidAsync(owner.Uuid, category.Uuid);

        Assert.Null(seenByStranger); // resource-owned: existence is not leaked
        Assert.NotNull(seenByOwner);
    }

    [SkippableFact]
    public async Task GetByUuidAsync_SoftDeletedOwnedCategory_IsStillReturned()
    {
        var user = await SeedUserAsync();
        var category = await SeedCategoryAsync(user.Id, "Ăn uống", deleted: true);

        var found = await CreateRepository().GetByUuidAsync(user.Uuid, category.Uuid);

        Assert.NotNull(found); // callers decide (the delete guard needs to see IsDefault of a deleted row)
        Assert.True(found!.IsDeleted);
    }

    [SkippableFact]
    public async Task ListByUserAsync_ReturnsOnlyTheCallersCategories()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        await SeedCategoryAsync(owner.Id, "Owned");
        await SeedCategoryAsync(stranger.Id, "Stranger");

        var list = await CreateRepository().ListByUserAsync(owner.Uuid, includeDeleted: false);

        Assert.Equal(["Owned"], list.Select(category => category.Name));
    }

    [SkippableFact]
    public async Task ListByUserAsync_DefaultExcludesSoftDeleted_IncludeDeletedShowsThem()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Active");
        await SeedCategoryAsync(user.Id, "Deleted", deleted: true);

        var defaultList = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: false);
        var fullList = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: true);

        Assert.Equal(["Active"], defaultList.Select(category => category.Name)); // hidden from selection
        Assert.Equal(2, fullList.Count); // history preserved
        Assert.True(fullList.Single(category => category.Name == "Deleted").IsDeleted);
    }

    [SkippableFact]
    public async Task ListByUserAsync_SortsDefaultFirstThenNameAscending()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Zoe");
        await SeedCategoryAsync(user.Id, "Anna");
        await SeedCategoryAsync(user.Id, "Mặc định", isDefault: true);
        await SeedCategoryAsync(user.Id, "Minh");

        var list = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: false);

        Assert.True(list[0].IsDefault); // OQ11: default always first
        Assert.Equal(["Mặc định", "Anna", "Minh", "Zoe"], list.Select(category => category.Name)); // then A->Z
    }

    [SkippableFact]
    public async Task UpdateAsync_OwnedCategory_PersistsNameColorIcon()
    {
        var user = await SeedUserAsync();
        var category = await SeedCategoryAsync(user.Id, "Đi lại", color: "#111111", icon: "old");

        var result = await CreateRepository().UpdateAsync(user.Uuid, category.Uuid, "Di chuyển", Blue, "🚗");

        Assert.Equal(NameWriteStatus.Updated, result.Status);
        var persisted = await ReloadAsync(category.Uuid);
        Assert.Equal("Di chuyển", persisted.Name);
        Assert.Equal(Blue, persisted.Color);
        Assert.Equal("🚗", persisted.Icon);
    }

    [SkippableFact]
    public async Task UpdateAsync_AnotherUsersCategory_ReturnsNotFoundAndDoesNotChangeIt()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var category = await SeedCategoryAsync(owner.Id, "Đi lại");

        var result = await CreateRepository().UpdateAsync(stranger.Uuid, category.Uuid, "Hacked", Blue, null);

        Assert.Equal(NameWriteStatus.NotFound, result.Status);
        Assert.Equal("Đi lại", (await ReloadAsync(category.Uuid)).Name);
    }

    [SkippableFact]
    public async Task UpdateAsync_CollidingWithAnotherActiveName_ReturnsNameDuplicate()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Ăn uống");
        var target = await SeedCategoryAsync(user.Id, "Đi lại");

        var result = await CreateRepository().UpdateAsync(user.Uuid, target.Uuid, "Ăn uống", Blue, null);

        Assert.Equal(NameWriteStatus.NameDuplicate, result.Status);
    }

    [SkippableFact]
    public async Task SoftDeleteAsync_OwnedCategory_SetsFlagButKeepsTheRow()
    {
        var user = await SeedUserAsync();
        var category = await SeedCategoryAsync(user.Id, "Đi lại");

        var deleted = await CreateRepository().SoftDeleteAsync(user.Uuid, category.Uuid);

        Assert.True(deleted);
        Assert.True((await ReloadAsync(category.Uuid)).IsDeleted); // row still exists, just flagged
    }

    [SkippableFact]
    public async Task SoftDeleteAsync_AnotherUsersCategory_ReturnsFalseAndLeavesItActive()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var category = await SeedCategoryAsync(owner.Id, "Đi lại");

        var result = await CreateRepository().SoftDeleteAsync(stranger.Uuid, category.Uuid);

        Assert.False(result);
        Assert.False((await ReloadAsync(category.Uuid)).IsDeleted);
    }

    [SkippableFact]
    public async Task SetDefaultAsync_ClearsThePreviousDefaultAndSetsTheTargetAtomically()
    {
        var user = await SeedUserAsync();
        var oldDefault = await SeedCategoryAsync(user.Id, "Ăn uống", isDefault: true);
        var target = await SeedCategoryAsync(user.Id, "Đi lại");

        var result = await CreateRepository().SetDefaultAsync(user.Uuid, target.Uuid);

        Assert.True(result);
        Assert.False((await ReloadAsync(oldDefault.Uuid)).IsDefault); // exactly the old one cleared
        Assert.True((await ReloadAsync(target.Uuid)).IsDefault);

        // Never zero, never two: exactly one default remains.
        await using var context = CreateContext();
        Assert.Equal(1, await context.Categories.CountAsync(category => category.UserId == user.Id && category.IsDefault && !category.IsDeleted));
    }

    [SkippableFact]
    public async Task SetDefaultAsync_SoftDeletedTarget_ReturnsFalse()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Ăn uống", isDefault: true);
        var deletedTarget = await SeedCategoryAsync(user.Id, "Đi lại", deleted: true);

        var result = await CreateRepository().SetDefaultAsync(user.Uuid, deletedTarget.Uuid);

        Assert.False(result); // a soft-deleted category cannot be made default
        Assert.False((await ReloadAsync(deletedTarget.Uuid)).IsDefault);
    }

    [SkippableFact]
    public async Task SetDefaultAsync_AnotherUsersCategory_ReturnsFalse()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var category = await SeedCategoryAsync(owner.Id, "Ăn uống");

        var result = await CreateRepository().SetDefaultAsync(stranger.Uuid, category.Uuid);

        Assert.False(result); // resource-owned: a foreign category is invisible
    }

    [SkippableFact]
    public async Task GetUserUuidsWithoutDefaultCategoryAsync_IncludesLackingUsersAndExcludesEquippedOnes()
    {
        var lacking = await SeedUserAsync();
        var equipped = await SeedUserAsync();
        await SeedCategoryAsync(equipped.Id, "Ăn uống", isDefault: true);

        var uuids = await CreateRepository().GetUserUuidsWithoutDefaultCategoryAsync();

        Assert.Contains(lacking.Uuid, uuids);
        Assert.DoesNotContain(equipped.Uuid, uuids);
    }

    [SkippableFact]
    public async Task GetUserUuidsWithoutDefaultCategoryAsync_TreatsUserWithOnlySoftDeletedDefaultAsMissing()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Ăn uống", isDefault: true, deleted: true); // deleted -> not an ACTIVE default

        var uuids = await CreateRepository().GetUserUuidsWithoutDefaultCategoryAsync();

        Assert.Contains(user.Uuid, uuids);
    }

    [SkippableFact]
    public async Task SeedSuggestedOrElectDefaultAsync_CategoryLessUser_SeedsTheFiveSuggestedWithOneDefault()
    {
        var user = await SeedUserAsync();

        var seeded = await CreateRepository().SeedSuggestedOrElectDefaultAsync(user.Uuid);

        Assert.True(seeded);
        await using var context = CreateContext();
        var categories = await context.Categories.AsNoTracking().Where(category => category.UserId == user.Id).ToListAsync();
        Assert.Equal(5, categories.Count);
        var defaultCategory = Assert.Single(categories, category => category.IsDefault);
        Assert.Equal("Ăn uống", defaultCategory.Name);
    }

    [SkippableFact]
    public async Task SeedSuggestedOrElectDefaultAsync_UserWithActivesButNoDefault_ElectsOneWithoutSeeding()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Chi khác");

        var fixedUp = await CreateRepository().SeedSuggestedOrElectDefaultAsync(user.Uuid);

        Assert.True(fixedUp);
        await using var context = CreateContext();
        var categories = await context.Categories.AsNoTracking().Where(category => category.UserId == user.Id).ToListAsync();
        Assert.Single(categories); // no suggested set added - an existing category was elected
        Assert.True(categories[0].IsDefault);
    }

    [SkippableFact]
    public async Task SeedSuggestedOrElectDefaultAsync_UserAlreadyHasDefault_IsNoOp()
    {
        var user = await SeedUserAsync();
        await SeedCategoryAsync(user.Id, "Ăn uống", isDefault: true);

        var changed = await CreateRepository().SeedSuggestedOrElectDefaultAsync(user.Uuid);

        Assert.False(changed);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Categories.CountAsync(category => category.UserId == user.Id));
    }
}
