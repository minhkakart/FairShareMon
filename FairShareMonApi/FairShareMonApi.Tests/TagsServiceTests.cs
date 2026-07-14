using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Tags;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Tags;
using FairShareMonApi.Validators.Tags;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>TagsService</c> over a fake <see cref="ITagRepository"/> (that re-implements
/// the atomic create-path decision tree in memory) plus the real AutoMapper profile and real
/// validators (no DB). Proves: create trims the name; active-dup → 5001; soft-deleted-name →
/// reactivate (same row, name-only); rename dup → 5001 and miss → 5000; delete miss → 5000;
/// <c>includeDeleted</c> passthrough.
/// </summary>
public class TagsServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000ce01";

    private readonly FakeTagRepository _repository = new();
    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<TagProfile>()).CreateMapper();

    private TagsService CreateService() =>
        new(_repository, _mapper, new CreateTagRequestValidator(), new UpdateTagRequestValidator());

    private Tag AddTag(string name, bool deleted = false)
    {
        var tag = new Tag { Name = name, IsDeleted = deleted };
        _repository.Tags.Add((UserUuid, tag));
        return tag;
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_InsertsActiveTag()
    {
        var response = await CreateService().CreateAsync(UserUuid, new CreateTagRequest { Name = "Công tác" });

        Assert.Equal("Công tác", response.Name);
        Assert.False(response.IsDeleted);
        Assert.Single(_repository.Tags);
    }

    [Fact]
    public async Task CreateAsync_NameWithSurroundingWhitespace_IsTrimmed()
    {
        var response = await CreateService().CreateAsync(UserUuid, new CreateTagRequest { Name = "   Du lịch   " });

        Assert.Equal("Du lịch", response.Name);
    }

    [Fact]
    public async Task CreateAsync_ActiveNameCollision_ThrowsTagNameDuplicate5001()
    {
        AddTag("Công tác");

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, new CreateTagRequest { Name = "Công tác" }));

        Assert.Equal(ErrorCodes.TagNameDuplicate, exception.Code);
    }

    [Fact]
    public async Task CreateAsync_SoftDeletedNameMatch_ReactivatesSameRow()
    {
        var deleted = AddTag("Công tác", deleted: true);

        var response = await CreateService().CreateAsync(UserUuid, new CreateTagRequest { Name = "Công tác" });

        Assert.Equal(deleted.Uuid, response.Uuid); // same row revived (relink history), not a duplicate
        Assert.False(response.IsDeleted);
        Assert.Single(_repository.Tags);
    }

    [Fact]
    public async Task CreateAsync_InvalidName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().CreateAsync(UserUuid, new CreateTagRequest { Name = "" }));
    }

    [Fact]
    public async Task CreateAsync_UnknownUser_ThrowsTagNotFound()
    {
        _repository.FailCreateWithUnknownUser = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, new CreateTagRequest { Name = "Công tác" }));

        Assert.Equal(ErrorCodes.TagNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Miss_ThrowsTagNotFound5000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetAsync(UserUuid, "no-such-tag"));

        Assert.Equal(ErrorCodes.TagNotFound, exception.Code);
    }

    [Fact]
    public async Task RenameAsync_Miss_ThrowsTagNotFound5000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().RenameAsync(UserUuid, "no-such-tag", new UpdateTagRequest { Name = "X" }));

        Assert.Equal(ErrorCodes.TagNotFound, exception.Code);
    }

    [Fact]
    public async Task RenameAsync_ActiveNameCollisionWithAnother_ThrowsTagNameDuplicate5001()
    {
        AddTag("Công tác");
        var target = AddTag("Du lịch");

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().RenameAsync(UserUuid, target.Uuid, new UpdateTagRequest { Name = "Công tác" }));

        Assert.Equal(ErrorCodes.TagNameDuplicate, exception.Code);
    }

    [Fact]
    public async Task RenameAsync_ValidChange_TrimsAndPersists()
    {
        var target = AddTag("Du lịch");

        var response = await CreateService().RenameAsync(UserUuid, target.Uuid, new UpdateTagRequest { Name = "  Nghỉ mát  " });

        Assert.Equal("Nghỉ mát", response.Name);
    }

    [Fact]
    public async Task DeleteAsync_Miss_ThrowsTagNotFound5000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DeleteAsync(UserUuid, "no-such-tag"));

        Assert.Equal(ErrorCodes.TagNotFound, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_OwnedTag_SoftDeletes()
    {
        var tag = AddTag("Du lịch");

        await CreateService().DeleteAsync(UserUuid, tag.Uuid);

        Assert.Contains(tag.Uuid, _repository.SoftDeletedUuids);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_PassesIncludeDeletedThrough(bool includeDeleted)
    {
        AddTag("Công tác");

        await CreateService().ListAsync(UserUuid, includeDeleted);

        Assert.Equal(includeDeleted, _repository.LastListIncludeDeleted);
    }

    /// <summary>
    /// In-memory stand-in for the tags table. Mirrors the repository's atomic create-path tree
    /// (active-dup → duplicate; soft-deleted match → reactivate; else insert) and rename uniqueness
    /// re-check so the service's mapping/trim behavior can be proven without a DB.
    /// </summary>
    private sealed class FakeTagRepository : ITagRepository
    {
        public List<(string UserUuid, Tag Tag)> Tags { get; } = [];

        public List<string> SoftDeletedUuids { get; } = [];

        public bool? LastListIncludeDeleted { get; private set; }

        public bool FailCreateWithUnknownUser { get; set; }

        public Task<IReadOnlyList<Tag>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default)
        {
            LastListIncludeDeleted = includeDeleted;
            var tags = Tags
                .Where(entry => entry.UserUuid == userUuid && (includeDeleted || !entry.Tag.IsDeleted))
                .Select(entry => entry.Tag)
                .ToList();
            return Task.FromResult<IReadOnlyList<Tag>>(tags);
        }

        public Task<Tag?> GetByUuidAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tags
                .Where(entry => entry.UserUuid == userUuid && entry.Tag.Uuid == tagUuid)
                .Select(entry => entry.Tag)
                .FirstOrDefault());

        public Task<Tag?> FindActiveByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tags
                .Where(entry => entry.UserUuid == userUuid && !entry.Tag.IsDeleted && entry.Tag.Name == name)
                .Select(entry => entry.Tag)
                .FirstOrDefault());

        public Task<Tag?> FindDeletedByNameAsync(string userUuid, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tags
                .Where(entry => entry.UserUuid == userUuid && entry.Tag.IsDeleted && entry.Tag.Name == name)
                .Select(entry => entry.Tag)
                .FirstOrDefault());

        public Task<NameWriteResult<Tag>> CreateAsync(string userUuid, string name, CancellationToken cancellationToken = default)
        {
            if (FailCreateWithUnknownUser)
                return Task.FromResult(NameWriteResult<Tag>.NotFound());

            var active = Tags
                .Where(entry => entry.UserUuid == userUuid && !entry.Tag.IsDeleted && entry.Tag.Name == name)
                .Select(entry => entry.Tag)
                .FirstOrDefault();
            if (active is not null)
                return Task.FromResult(NameWriteResult<Tag>.NameDuplicate());

            var deleted = Tags
                .Where(entry => entry.UserUuid == userUuid && entry.Tag.IsDeleted && entry.Tag.Name == name)
                .Select(entry => entry.Tag)
                .FirstOrDefault();
            if (deleted is not null)
            {
                deleted.IsDeleted = false; // name-only: nothing else to set
                return Task.FromResult(NameWriteResult<Tag>.Reactivated(deleted));
            }

            var tag = new Tag { Name = name };
            Tags.Add((userUuid, tag));
            return Task.FromResult(NameWriteResult<Tag>.Created(tag));
        }

        public Task<NameWriteResult<Tag>> RenameAsync(string userUuid, string tagUuid, string name, CancellationToken cancellationToken = default)
        {
            var tag = Tags
                .Where(entry => entry.UserUuid == userUuid && !entry.Tag.IsDeleted && entry.Tag.Uuid == tagUuid)
                .Select(entry => entry.Tag)
                .FirstOrDefault();
            if (tag is null)
                return Task.FromResult(NameWriteResult<Tag>.NotFound());

            var duplicate = Tags.Any(entry => entry.UserUuid == userUuid
                && !entry.Tag.IsDeleted
                && entry.Tag.Uuid != tagUuid
                && entry.Tag.Name == name);
            if (duplicate)
                return Task.FromResult(NameWriteResult<Tag>.NameDuplicate());

            tag.Name = name;
            return Task.FromResult(NameWriteResult<Tag>.Updated(tag));
        }

        public Task<bool> SoftDeleteAsync(string userUuid, string tagUuid, CancellationToken cancellationToken = default)
        {
            var tag = Tags
                .Where(entry => entry.UserUuid == userUuid && entry.Tag.Uuid == tagUuid)
                .Select(entry => entry.Tag)
                .FirstOrDefault();
            if (tag is null)
                return Task.FromResult(false);

            tag.IsDeleted = true;
            SoftDeletedUuids.Add(tagUuid);
            return Task.FromResult(true);
        }

        public IQueryable<Tag> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(
            Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(
            Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
