using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Members;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Validators.Members;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>MembersService</c> over a fake <see cref="IMemberRepository"/> and the
/// real AutoMapper profile + real validators (no DB). Proves: create never sets owner-rep and
/// trims; duplicate names accepted (OQ6); owner-rep delete → 3001 without touching storage; misses
/// → 3000; owner-rep rename allowed; <c>includeDeleted</c> passthrough; idempotent backfill.
/// </summary>
public class MembersServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000ab01";

    private readonly FakeMemberRepository _repository = new();
    private readonly FakeTierService _tier = new();
    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<MemberProfile>()).CreateMapper();

    private MembersService CreateService() =>
        new(_repository, _tier, _mapper, new CreateMemberRequestValidator(), new UpdateMemberRequestValidator());

    private Member AddMember(string name, bool ownerRep = false, bool deleted = false)
    {
        var member = new Member { Name = name, IsOwnerRepresentative = ownerRep, IsDeleted = deleted };
        _repository.Members.Add((UserUuid, member));
        return member;
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesActiveNonOwnerRepMember()
    {
        var response = await CreateService().CreateAsync(UserUuid, new CreateMemberRequest { Name = "An" });

        Assert.Equal("An", response.Name);
        Assert.False(response.IsOwnerRepresentative); // an API-created member is never the owner-rep
        Assert.False(response.IsDeleted);
        var stored = Assert.Single(_repository.Members);
        Assert.False(stored.Member.IsOwnerRepresentative);
    }

    [Fact]
    public async Task CreateAsync_NameWithSurroundingWhitespace_IsTrimmed()
    {
        var response = await CreateService().CreateAsync(UserUuid, new CreateMemberRequest { Name = "   Bình   " });

        Assert.Equal("Bình", response.Name);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_IsAccepted()
    {
        var service = CreateService();

        await service.CreateAsync(UserUuid, new CreateMemberRequest { Name = "An" });
        await service.CreateAsync(UserUuid, new CreateMemberRequest { Name = "An" }); // OQ6: no uniqueness

        Assert.Equal(2, _repository.Members.Count);
    }

    [Fact]
    public async Task CreateAsync_InvalidName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().CreateAsync(UserUuid, new CreateMemberRequest { Name = "" }));
    }

    [Fact]
    public async Task CreateAsync_TierLimitReached_ThrowsMemberLimitReached13000()
    {
        _tier.MemberLimitCode = ErrorCodes.MemberLimitReached; // M10: the guard fires for a Free caller at the cap

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, new CreateMemberRequest { Name = "An" }));

        Assert.Equal(ErrorCodes.MemberLimitReached, exception.Code);
        Assert.Empty(_repository.Members); // guard fires before the repository insert
    }

    [Fact]
    public async Task CreateAsync_UnknownUser_ThrowsMemberNotFound()
    {
        _repository.FailCreateWithUnknownUser = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, new CreateMemberRequest { Name = "An" }));

        Assert.Equal(ErrorCodes.MemberNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Miss_ThrowsMemberNotFound3000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetAsync(UserUuid, "no-such-member"));

        Assert.Equal(ErrorCodes.MemberNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Found_ReturnsResponse()
    {
        var member = AddMember("An");

        var response = await CreateService().GetAsync(UserUuid, member.Uuid);

        Assert.Equal(member.Uuid, response.Uuid);
        Assert.Equal("An", response.Name);
    }

    [Fact]
    public async Task RenameAsync_Miss_ThrowsMemberNotFound3000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().RenameAsync(UserUuid, "no-such-member", new UpdateMemberRequest { Name = "X" }));

        Assert.Equal(ErrorCodes.MemberNotFound, exception.Code);
    }

    [Fact]
    public async Task RenameAsync_OwnerRepresentative_IsAllowed()
    {
        var ownerRep = AddMember(Member.OwnerRepresentativeDefaultName, ownerRep: true);

        var response = await CreateService().RenameAsync(UserUuid, ownerRep.Uuid, new UpdateMemberRequest { Name = "Chủ sổ thật" });

        Assert.Equal("Chủ sổ thật", response.Name); // OQ4: owner-rep is renamable
        Assert.True(response.IsOwnerRepresentative);
    }

    [Fact]
    public async Task RenameAsync_TrimsName()
    {
        var member = AddMember("An");

        var response = await CreateService().RenameAsync(UserUuid, member.Uuid, new UpdateMemberRequest { Name = "  Bình  " });

        Assert.Equal("Bình", response.Name);
    }

    [Fact]
    public async Task DeleteAsync_OwnerRepresentative_Throws3001AndDoesNotSoftDelete()
    {
        var ownerRep = AddMember(Member.OwnerRepresentativeDefaultName, ownerRep: true);

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().DeleteAsync(UserUuid, ownerRep.Uuid));

        Assert.Equal(ErrorCodes.OwnerRepresentativeNotDeletable, exception.Code);
        Assert.Empty(_repository.SoftDeletedUuids); // guard fires before any soft-delete
        Assert.False(ownerRep.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_Miss_ThrowsMemberNotFound3000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().DeleteAsync(UserUuid, "no-such-member"));

        Assert.Equal(ErrorCodes.MemberNotFound, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_RegularMember_SoftDeletes()
    {
        var member = AddMember("An");

        await CreateService().DeleteAsync(UserUuid, member.Uuid);

        Assert.Contains(member.Uuid, _repository.SoftDeletedUuids);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_PassesIncludeDeletedThrough(bool includeDeleted)
    {
        AddMember("An");

        await CreateService().ListAsync(UserUuid, includeDeleted);

        Assert.Equal(includeDeleted, _repository.LastListIncludeDeleted);
    }

    [Fact]
    public async Task EnsureOwnerRepresentativeForAllAsync_CreatesOwnerRepNamedToiForEachMissingUser()
    {
        _repository.UsersWithoutOwnerRep.AddRange(["user-a", "user-b"]);

        var created = await CreateService().EnsureOwnerRepresentativeForAllAsync();

        Assert.Equal(2, created);
        Assert.Equal(2, _repository.Members.Count);
        Assert.All(_repository.Members, entry =>
        {
            Assert.True(entry.Member.IsOwnerRepresentative);
            Assert.Equal(Member.OwnerRepresentativeDefaultName, entry.Member.Name);
        });
    }

    [Fact]
    public async Task EnsureOwnerRepresentativeForAllAsync_NoMissingUsers_CreatesNothing()
    {
        var created = await CreateService().EnsureOwnerRepresentativeForAllAsync();

        Assert.Equal(0, created);
        Assert.Empty(_repository.Members);
    }

    /// <summary>In-memory stand-in for the members table; only the surface MembersService calls is real.</summary>
    private sealed class FakeMemberRepository : IMemberRepository
    {
        public List<(string UserUuid, Member Member)> Members { get; } = [];

        public List<string> SoftDeletedUuids { get; } = [];

        public List<string> UsersWithoutOwnerRep { get; } = [];

        public bool? LastListIncludeDeleted { get; private set; }

        public bool FailCreateWithUnknownUser { get; set; }

        public Task<IReadOnlyList<Member>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default)
        {
            LastListIncludeDeleted = includeDeleted;
            var members = Members
                .Where(entry => entry.UserUuid == userUuid && (includeDeleted || !entry.Member.IsDeleted))
                .Select(entry => entry.Member)
                .ToList();
            return Task.FromResult<IReadOnlyList<Member>>(members);
        }

        public Task<Member?> GetByUuidAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Members
                .Where(entry => entry.UserUuid == userUuid && entry.Member.Uuid == memberUuid)
                .Select(entry => entry.Member)
                .FirstOrDefault());

        public Task<Member?> CreateAsync(string userUuid, Member member, CancellationToken cancellationToken = default)
        {
            if (FailCreateWithUnknownUser)
                return Task.FromResult<Member?>(null);

            Members.Add((userUuid, member));
            return Task.FromResult<Member?>(member);
        }

        public Task<Member?> RenameAsync(string userUuid, string memberUuid, string name, CancellationToken cancellationToken = default)
        {
            var member = Members
                .Where(entry => entry.UserUuid == userUuid && entry.Member.Uuid == memberUuid)
                .Select(entry => entry.Member)
                .FirstOrDefault();
            if (member is null)
                return Task.FromResult<Member?>(null);

            member.Name = name;
            return Task.FromResult<Member?>(member);
        }

        public Task<bool> SoftDeleteAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default)
        {
            var member = Members
                .Where(entry => entry.UserUuid == userUuid && entry.Member.Uuid == memberUuid)
                .Select(entry => entry.Member)
                .FirstOrDefault();
            if (member is null)
                return Task.FromResult(false);

            member.IsDeleted = true;
            SoftDeletedUuids.Add(memberUuid);
            return Task.FromResult(true);
        }

        public Task<bool> HasOwnerRepresentativeAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Members.Any(entry => entry.UserUuid == userUuid && entry.Member.IsOwnerRepresentative && !entry.Member.IsDeleted));

        public Task<int> CountActiveByUserAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Members.Count(entry => entry.UserUuid == userUuid && !entry.Member.IsDeleted));

        public Task<IReadOnlyList<string>> GetUserUuidsWithoutOwnerRepresentativeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(UsersWithoutOwnerRep.ToList());

        public IQueryable<Member> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(
            Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(
            Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
