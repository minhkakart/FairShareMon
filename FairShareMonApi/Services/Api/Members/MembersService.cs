using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Members;

/// <summary>
/// Business logic for The-ideal.md §2 / §3.2 (Quản lý thành viên): list / get / create / rename /
/// soft-delete members, all resource-owned (an ownership miss -&gt; <c>MemberNotFound</c> 404, never
/// 403). Also owns the owner-representative invariant: created on registration (via the bootstrap
/// seam), renamable but never deletable, and backfilled idempotently for pre-existing users.
/// </summary>
public interface IMembersService
{
    Task<IReadOnlyList<MemberResponse>> ListAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default);

    Task<MemberResponse> GetAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default);

    Task<MemberResponse> CreateAsync(string userUuid, CreateMemberRequest request, CancellationToken cancellationToken = default);

    Task<MemberResponse> RenameAsync(string userUuid, string memberUuid, UpdateMemberRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default);

    /// <summary>Idempotent backfill: gives every user lacking an active owner-representative member one. Returns how many were created.</summary>
    Task<int> EnsureOwnerRepresentativeForAllAsync(CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IMembersService))]
public sealed class MembersService(
    IMemberRepository memberRepository,
    IMapper mapper,
    IValidator<CreateMemberRequest> createValidator,
    IValidator<UpdateMemberRequest> updateValidator) : IMembersService
{
    public async Task<IReadOnlyList<MemberResponse>> ListAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var members = await memberRepository.ListByUserAsync(userUuid, includeDeleted, cancellationToken);
        return mapper.Map<IReadOnlyList<MemberResponse>>(members);
    }

    public async Task<MemberResponse> GetAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default)
    {
        var member = await memberRepository.GetByUuidAsync(userUuid, memberUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<MemberResponse>(member);
    }

    public async Task<MemberResponse> CreateAsync(string userUuid, CreateMemberRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        // A member created through the API is never the owner-representative (that one is bootstrapped).
        var member = new Member { Name = request.Name.Trim() };
        var created = await memberRepository.CreateAsync(userUuid, member, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<MemberResponse>(created);
    }

    public async Task<MemberResponse> RenameAsync(string userUuid, string memberUuid, UpdateMemberRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        // Renaming the owner-representative member is allowed (OQ4).
        var member = await memberRepository.RenameAsync(userUuid, memberUuid, request.Name.Trim(), cancellationToken)
            ?? throw NotFound();

        return mapper.Map<MemberResponse>(member);
    }

    public async Task DeleteAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default)
    {
        var member = await memberRepository.GetByUuidAsync(userUuid, memberUuid, cancellationToken)
            ?? throw NotFound();

        if (member.IsOwnerRepresentative)
            throw new ErrorException(ErrorCodes.OwnerRepresentativeNotDeletable, "Không thể xóa thành viên đại diện chủ sổ.");

        await memberRepository.SoftDeleteAsync(userUuid, memberUuid, cancellationToken);
    }

    public async Task<int> EnsureOwnerRepresentativeForAllAsync(CancellationToken cancellationToken = default)
    {
        var userUuids = await memberRepository.GetUserUuidsWithoutOwnerRepresentativeAsync(cancellationToken);

        var created = 0;
        foreach (var userUuid in userUuids)
        {
            var member = new Member
            {
                Name = Member.OwnerRepresentativeDefaultName,
                IsOwnerRepresentative = true
            };
            if (await memberRepository.CreateAsync(userUuid, member, cancellationToken) is not null)
                created++;
        }

        return created;
    }

    private static ErrorException NotFound() =>
        new(ErrorCodes.MemberNotFound, "Không tìm thấy thành viên.");
}
