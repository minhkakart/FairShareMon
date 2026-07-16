using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Shares;

/// <summary>
/// Business logic for the individual share sub-routes on an expense (The-ideal.md §3.5): add / update
/// (incl. change-member) / delete, all resource-owned via the owning expense (an ownership miss -&gt;
/// 404, never 403). Maps the repository's typed write result to the right 6xxx/7xxx
/// <c>ErrorException</c>; enforces the owner-representative-share protection (§5, OQ4).
/// </summary>
public interface ISharesService
{
    Task<ShareResponse> AddAsync(string userUuid, string expenseUuid, CreateShareRequest request, CancellationToken cancellationToken = default);

    Task<ShareResponse> UpdateAsync(string userUuid, string expenseUuid, string shareUuid, UpdateShareRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userUuid, string expenseUuid, string shareUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(ISharesService))]
public sealed class SharesService(
    IShareRepository shareRepository,
    IMapper mapper,
    IValidator<CreateShareRequest> createValidator,
    IValidator<UpdateShareRequest> updateValidator) : ISharesService
{
    public async Task<ShareResponse> AddAsync(string userUuid, string expenseUuid, CreateShareRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var data = new ShareData(request.MemberUuid, request.Amount, request.Note?.Trim());
        var result = await shareRepository.AddAsync(userUuid, expenseUuid, data, cancellationToken);

        switch (result.Status)
        {
            case ExpenseWriteStatus.Success:
                return mapper.Map<ShareResponse>(result.Entity);
            case ExpenseWriteStatus.ShareMemberInvalid:
                throw ShareMemberInvalid();
            case ExpenseWriteStatus.DuplicateShareMember:
                throw DuplicateShareMember();
            case ExpenseWriteStatus.EventClosed:
                throw EventClosed();
            default:
                throw ExpenseNotFound();
        }
    }

    public async Task<ShareResponse> UpdateAsync(string userUuid, string expenseUuid, string shareUuid, UpdateShareRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var data = new ShareData(request.MemberUuid, request.Amount, request.Note?.Trim());
        var result = await shareRepository.UpdateAsync(userUuid, expenseUuid, shareUuid, data, cancellationToken);

        switch (result.Status)
        {
            case ExpenseWriteStatus.Success:
                return mapper.Map<ShareResponse>(result.Entity);
            case ExpenseWriteStatus.ShareMemberInvalid:
                throw ShareMemberInvalid();
            case ExpenseWriteStatus.DuplicateShareMember:
                throw DuplicateShareMember();
            case ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable:
                throw new ErrorException(ErrorCodes.OwnerRepresentativeShareNotDeletable, MessageKeys.Error.OwnerRepresentativeShareMemberNotChangeable);
            case ExpenseWriteStatus.EventClosed:
                throw EventClosed();
            default:
                throw ShareNotFound();
        }
    }

    public async Task DeleteAsync(string userUuid, string expenseUuid, string shareUuid, CancellationToken cancellationToken = default)
    {
        var status = await shareRepository.DeleteAsync(userUuid, expenseUuid, shareUuid, cancellationToken);

        switch (status)
        {
            case ExpenseWriteStatus.Success:
                return;
            case ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable:
                throw new ErrorException(ErrorCodes.OwnerRepresentativeShareNotDeletable, MessageKeys.Error.OwnerRepresentativeShareNotDeletable);
            case ExpenseWriteStatus.EventClosed:
                throw EventClosed();
            default:
                throw ShareNotFound();
        }
    }

    private static ErrorException ExpenseNotFound() =>
        new(ErrorCodes.ExpenseNotFound, MessageKeys.Error.ExpenseNotFound);

    private static ErrorException ShareNotFound() =>
        new(ErrorCodes.ShareNotFound, MessageKeys.Error.ShareNotFound);

    private static ErrorException ShareMemberInvalid() =>
        new(ErrorCodes.ShareMemberInvalid, MessageKeys.Error.ShareMemberInvalid);

    private static ErrorException DuplicateShareMember() =>
        new(ErrorCodes.DuplicateShareMember, MessageKeys.Error.DuplicateShareMember);

    private static ErrorException EventClosed() =>
        new(ErrorCodes.EventClosed, MessageKeys.Error.EventClosed);
}
