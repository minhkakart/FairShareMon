using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>TagRepository</c> against the real MariaDB (skippable). Covers entity
/// defaults (uuid/UTC/user_id), resource-owned scoping (another user's tag invisible on
/// get/list/rename/delete), soft-delete-preserves-the-row, the "unique active name" rule +
/// accent/case-insensitive collision (OQ5), reactivation-on-name-reuse (§3.4/§5, same row revived),
/// and the OQ11 A→Z sort.
/// </summary>
[Collection("AuthIntegration")]
public class TagRepositoryTests(DatabaseFixture fixture) : AuthDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private TagRepository CreateRepository() => new(CreateContext());

    private async Task<Tag> SeedTagAsync(ulong userId, string name, bool deleted = false)
    {
        await using var context = CreateContext();
        var tag = new Tag { UserId = userId, Name = name, IsDeleted = deleted };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag;
    }

    private async Task<Tag> ReloadAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.Tags.AsNoTracking().SingleAsync(tag => tag.Uuid == uuid);
    }

    [SkippableFact]
    public async Task CreateAsync_NewTag_PersistsWithUuidUtcUserIdAndActiveFlag()
    {
        var user = await SeedUserAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await CreateRepository().CreateAsync(user.Uuid, "Công tác");
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(NameWriteStatus.Created, result.Status);
        var created = result.Entity!;
        Assert.Equal(36, created.Uuid.Length);
        Assert.InRange(created.CreatedAt, before, after);
        Assert.False(created.IsDeleted);

        var persisted = await ReloadAsync(created.Uuid);
        Assert.Equal(user.Id, persisted.UserId);
    }

    [SkippableFact]
    public async Task CreateAsync_UnknownUser_ReturnsNotFound()
    {
        var result = await CreateRepository().CreateAsync("00000000-0000-7000-8000-000000000000", "Công tác");

        Assert.Equal(NameWriteStatus.NotFound, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_ActiveNameCollision_ReturnsNameDuplicate()
    {
        var user = await SeedUserAsync();
        await SeedTagAsync(user.Id, "Công tác");

        var result = await CreateRepository().CreateAsync(user.Uuid, "Công tác");

        Assert.Equal(NameWriteStatus.NameDuplicate, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_AccentAndCaseInsensitiveCollision_ReturnsNameDuplicate()
    {
        var user = await SeedUserAsync();
        await SeedTagAsync(user.Id, "Công tác");

        // OQ5: the utf8mb4_unicode_ci collation treats "cong tac" == "Công tác".
        var result = await CreateRepository().CreateAsync(user.Uuid, "cong tac");

        Assert.Equal(NameWriteStatus.NameDuplicate, result.Status);
    }

    [SkippableFact]
    public async Task CreateAsync_SoftDeletedNameReuse_ReactivatesSameRow()
    {
        var user = await SeedUserAsync();
        var deleted = await SeedTagAsync(user.Id, "Công tác", deleted: true);

        var result = await CreateRepository().CreateAsync(user.Uuid, "Công tác");

        Assert.Equal(NameWriteStatus.Reactivated, result.Status);
        Assert.Equal(deleted.Uuid, result.Entity!.Uuid); // same row revived (relink history)

        Assert.False((await ReloadAsync(deleted.Uuid)).IsDeleted);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Tags.CountAsync(tag => tag.UserId == user.Id)); // no duplicate created
    }

    [SkippableFact]
    public async Task GetByUuidAsync_AnotherUsersTag_ReturnsNull()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var tag = await SeedTagAsync(owner.Id, "Công tác");

        Assert.Null(await CreateRepository().GetByUuidAsync(stranger.Uuid, tag.Uuid)); // existence not leaked
        Assert.NotNull(await CreateRepository().GetByUuidAsync(owner.Uuid, tag.Uuid));
    }

    [SkippableFact]
    public async Task ListByUserAsync_ReturnsOnlyTheCallersTags()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        await SeedTagAsync(owner.Id, "Owned");
        await SeedTagAsync(stranger.Id, "Stranger");

        var list = await CreateRepository().ListByUserAsync(owner.Uuid, includeDeleted: false);

        Assert.Equal(["Owned"], list.Select(tag => tag.Name));
    }

    [SkippableFact]
    public async Task ListByUserAsync_DefaultExcludesSoftDeleted_IncludeDeletedShowsThem()
    {
        var user = await SeedUserAsync();
        await SeedTagAsync(user.Id, "Active");
        await SeedTagAsync(user.Id, "Deleted", deleted: true);

        var defaultList = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: false);
        var fullList = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: true);

        Assert.Equal(["Active"], defaultList.Select(tag => tag.Name));
        Assert.Equal(2, fullList.Count); // history preserved
    }

    [SkippableFact]
    public async Task ListByUserAsync_SortsNameAscending()
    {
        var user = await SeedUserAsync();
        await SeedTagAsync(user.Id, "Zoe");
        await SeedTagAsync(user.Id, "Anna");
        await SeedTagAsync(user.Id, "Minh");

        var list = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: false);

        Assert.Equal(["Anna", "Minh", "Zoe"], list.Select(tag => tag.Name)); // OQ11: A->Z
    }

    [SkippableFact]
    public async Task RenameAsync_OwnedTag_PersistsNewName()
    {
        var user = await SeedUserAsync();
        var tag = await SeedTagAsync(user.Id, "Công tác");

        var result = await CreateRepository().RenameAsync(user.Uuid, tag.Uuid, "Đi công tác");

        Assert.Equal(NameWriteStatus.Updated, result.Status);
        Assert.Equal("Đi công tác", (await ReloadAsync(tag.Uuid)).Name);
    }

    [SkippableFact]
    public async Task RenameAsync_AnotherUsersTag_ReturnsNotFoundAndDoesNotChangeIt()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var tag = await SeedTagAsync(owner.Id, "Công tác");

        var result = await CreateRepository().RenameAsync(stranger.Uuid, tag.Uuid, "Hacked");

        Assert.Equal(NameWriteStatus.NotFound, result.Status);
        Assert.Equal("Công tác", (await ReloadAsync(tag.Uuid)).Name);
    }

    [SkippableFact]
    public async Task RenameAsync_CollidingWithAnotherActiveName_ReturnsNameDuplicate()
    {
        var user = await SeedUserAsync();
        await SeedTagAsync(user.Id, "Công tác");
        var target = await SeedTagAsync(user.Id, "Du lịch");

        var result = await CreateRepository().RenameAsync(user.Uuid, target.Uuid, "Công tác");

        Assert.Equal(NameWriteStatus.NameDuplicate, result.Status);
    }

    [SkippableFact]
    public async Task SoftDeleteAsync_OwnedTag_SetsFlagButKeepsTheRow()
    {
        var user = await SeedUserAsync();
        var tag = await SeedTagAsync(user.Id, "Công tác");

        var deleted = await CreateRepository().SoftDeleteAsync(user.Uuid, tag.Uuid);

        Assert.True(deleted);
        Assert.True((await ReloadAsync(tag.Uuid)).IsDeleted); // row still exists, just flagged
    }

    [SkippableFact]
    public async Task SoftDeleteAsync_AnotherUsersTag_ReturnsFalseAndLeavesItActive()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var tag = await SeedTagAsync(owner.Id, "Công tác");

        var result = await CreateRepository().SoftDeleteAsync(stranger.Uuid, tag.Uuid);

        Assert.False(result);
        Assert.False((await ReloadAsync(tag.Uuid)).IsDeleted);
    }
}
