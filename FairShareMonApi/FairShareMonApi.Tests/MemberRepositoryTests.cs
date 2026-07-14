using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>MemberRepository</c> against the real MariaDB (skippable). Covers
/// entity defaults (uuid/UTC/user_id), resource-owned scoping (another user's member is invisible,
/// never returned), soft-delete-preserves-the-row semantics, the OQ8 sort, and the
/// bootstrap/backfill support queries.
/// </summary>
[Collection("AuthIntegration")]
public class MemberRepositoryTests(DatabaseFixture fixture) : AuthDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private MemberRepository CreateRepository() => new(CreateContext());

    private async Task<Member> SeedMemberAsync(ulong userId, string name, bool ownerRep = false, bool deleted = false)
    {
        await using var context = CreateContext();
        var member = new Member { UserId = userId, Name = name, IsOwnerRepresentative = ownerRep, IsDeleted = deleted };
        context.Members.Add(member);
        await context.SaveChangesAsync();
        return member;
    }

    [SkippableFact]
    public async Task CreateAsync_NewMember_PersistsWithUuidUtcTimestampAndActiveNonOwnerRepFlags()
    {
        var user = await SeedUserAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var created = await CreateRepository().CreateAsync(user.Uuid, new Member { Name = "An" });
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.NotNull(created);
        Assert.Equal(36, created!.Uuid.Length);
        Assert.InRange(created.CreatedAt, before, after); // AppDateTime.Now = UTC
        Assert.False(created.IsOwnerRepresentative);
        Assert.False(created.IsDeleted);

        await using var context = CreateContext();
        var persisted = await context.Members.AsNoTracking().SingleAsync(member => member.Uuid == created.Uuid);
        Assert.Equal(user.Id, persisted.UserId);
    }

    [SkippableFact]
    public async Task CreateAsync_UnknownUser_ReturnsNullAndPersistsNothing()
    {
        var created = await CreateRepository().CreateAsync("00000000-0000-7000-8000-000000000000", new Member { Name = "An" });

        Assert.Null(created);
    }

    [SkippableFact]
    public async Task GetByUuidAsync_AnotherUsersMember_ReturnsNull()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var member = await SeedMemberAsync(owner.Id, "An");

        // Resource-owned: the stranger cannot see the owner's member (existence is not leaked).
        var seenByStranger = await CreateRepository().GetByUuidAsync(stranger.Uuid, member.Uuid);
        var seenByOwner = await CreateRepository().GetByUuidAsync(owner.Uuid, member.Uuid);

        Assert.Null(seenByStranger);
        Assert.NotNull(seenByOwner);
    }

    [SkippableFact]
    public async Task GetByUuidAsync_SoftDeletedOwnedMember_IsStillReturned()
    {
        var user = await SeedUserAsync();
        var member = await SeedMemberAsync(user.Id, "An", deleted: true);

        var found = await CreateRepository().GetByUuidAsync(user.Uuid, member.Uuid);

        Assert.NotNull(found); // callers decide what to do with a deleted member
        Assert.True(found!.IsDeleted);
    }

    [SkippableFact]
    public async Task ListByUserAsync_ReturnsOnlyTheCallersMembers()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        await SeedMemberAsync(owner.Id, "Owned");
        await SeedMemberAsync(stranger.Id, "Stranger");

        var list = await CreateRepository().ListByUserAsync(owner.Uuid, includeDeleted: false);

        Assert.Equal(["Owned"], list.Select(member => member.Name));
    }

    [SkippableFact]
    public async Task ListByUserAsync_DefaultExcludesSoftDeleted_IncludeDeletedShowsThem()
    {
        var user = await SeedUserAsync();
        await SeedMemberAsync(user.Id, "Active");
        await SeedMemberAsync(user.Id, "Deleted", deleted: true);

        var defaultList = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: false);
        var fullList = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: true);

        Assert.Equal(["Active"], defaultList.Select(member => member.Name)); // hidden from selection
        Assert.Equal(2, fullList.Count); // history preserved
        Assert.True(fullList.Single(member => member.Name == "Deleted").IsDeleted);
    }

    [SkippableFact]
    public async Task ListByUserAsync_SortsOwnerRepFirstThenNameAscending()
    {
        var user = await SeedUserAsync();
        await SeedMemberAsync(user.Id, "Zoe");
        await SeedMemberAsync(user.Id, "Anna");
        await SeedMemberAsync(user.Id, "Chủ sổ", ownerRep: true);
        await SeedMemberAsync(user.Id, "Minh");

        var list = await CreateRepository().ListByUserAsync(user.Uuid, includeDeleted: false);

        Assert.True(list[0].IsOwnerRepresentative); // OQ8: owner-rep always first
        Assert.Equal(["Chủ sổ", "Anna", "Minh", "Zoe"], list.Select(member => member.Name)); // then A->Z
    }

    [SkippableFact]
    public async Task RenameAsync_OwnedMember_PersistsNewName()
    {
        var user = await SeedUserAsync();
        var member = await SeedMemberAsync(user.Id, "An");

        var renamed = await CreateRepository().RenameAsync(user.Uuid, member.Uuid, "Bình");

        Assert.NotNull(renamed);
        Assert.Equal("Bình", renamed!.Name);
        await using var context = CreateContext();
        Assert.Equal("Bình", (await context.Members.AsNoTracking().SingleAsync(existing => existing.Uuid == member.Uuid)).Name);
    }

    [SkippableFact]
    public async Task RenameAsync_AnotherUsersMember_ReturnsNullAndDoesNotChangeIt()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var member = await SeedMemberAsync(owner.Id, "An");

        var result = await CreateRepository().RenameAsync(stranger.Uuid, member.Uuid, "Hacked");

        Assert.Null(result);
        await using var context = CreateContext();
        Assert.Equal("An", (await context.Members.AsNoTracking().SingleAsync(existing => existing.Uuid == member.Uuid)).Name);
    }

    [SkippableFact]
    public async Task SoftDeleteAsync_OwnedMember_SetsFlagButKeepsTheRow()
    {
        var user = await SeedUserAsync();
        var member = await SeedMemberAsync(user.Id, "An");

        var deleted = await CreateRepository().SoftDeleteAsync(user.Uuid, member.Uuid);

        Assert.True(deleted);
        await using var context = CreateContext();
        var persisted = await context.Members.AsNoTracking().SingleAsync(existing => existing.Uuid == member.Uuid);
        Assert.True(persisted.IsDeleted); // row still exists, just flagged
    }

    [SkippableFact]
    public async Task SoftDeleteAsync_AnotherUsersMember_ReturnsFalseAndLeavesItActive()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var member = await SeedMemberAsync(owner.Id, "An");

        var result = await CreateRepository().SoftDeleteAsync(stranger.Uuid, member.Uuid);

        Assert.False(result);
        await using var context = CreateContext();
        Assert.False((await context.Members.AsNoTracking().SingleAsync(existing => existing.Uuid == member.Uuid)).IsDeleted);
    }

    [SkippableFact]
    public async Task SoftDeleteAsync_AlreadyDeletedOwnedMember_IsIdempotentSuccess()
    {
        var user = await SeedUserAsync();
        var member = await SeedMemberAsync(user.Id, "An", deleted: true);

        var result = await CreateRepository().SoftDeleteAsync(user.Uuid, member.Uuid);

        Assert.True(result); // re-deleting an owned member is a harmless no-op success
    }

    [SkippableFact]
    public async Task HasOwnerRepresentativeAsync_ReflectsPresenceOfAnActiveOwnerRep()
    {
        var withoutRep = await SeedUserAsync();
        var withRep = await SeedUserAsync();
        await SeedMemberAsync(withRep.Id, "Tôi", ownerRep: true);
        var repository = CreateRepository();

        Assert.False(await repository.HasOwnerRepresentativeAsync(withoutRep.Uuid));
        Assert.True(await repository.HasOwnerRepresentativeAsync(withRep.Uuid));
    }

    [SkippableFact]
    public async Task GetUserUuidsWithoutOwnerRepresentativeAsync_IncludesLackingUsersAndExcludesEquippedOnes()
    {
        var lacking = await SeedUserAsync();
        var equipped = await SeedUserAsync();
        await SeedMemberAsync(equipped.Id, "Tôi", ownerRep: true);

        var uuids = await CreateRepository().GetUserUuidsWithoutOwnerRepresentativeAsync();

        Assert.Contains(lacking.Uuid, uuids);
        Assert.DoesNotContain(equipped.Uuid, uuids);
    }

    [SkippableFact]
    public async Task GetUserUuidsWithoutOwnerRepresentativeAsync_TreatsSoftDeletedOwnerRepAsMissing()
    {
        var user = await SeedUserAsync();
        await SeedMemberAsync(user.Id, "Tôi", ownerRep: true, deleted: true); // deleted -> not an ACTIVE owner-rep

        var uuids = await CreateRepository().GetUserUuidsWithoutOwnerRepresentativeAsync();

        Assert.Contains(user.Uuid, uuids);
    }
}
