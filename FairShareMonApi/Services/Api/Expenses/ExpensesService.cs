using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Tiers;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Expenses;

/// <summary>
/// Business logic for The-ideal.md §3.5 / §3.8 (Phiếu chi tiêu &amp; nhật ký thay đổi): list / get /
/// create (atomic with shares) / update-general-info / delete / settled-toggle / history, all
/// resource-owned (an ownership miss -&gt; <c>ExpenseNotFound</c> 404, never 403). Maps the
/// repository's typed write result to the right 6xxx/7xxx <c>ErrorException</c>. The settled toggle is
/// a dedicated method (the seam for M6's closed-event exception, §4.4).
/// </summary>
public interface IExpensesService
{
    Task<IReadOnlyList<ExpenseSummaryResponse>> ListAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default);

    Task<ExpenseResponse> GetAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);

    Task<ExpenseResponse> CreateAsync(string userUuid, CreateExpenseRequest request, CancellationToken cancellationToken = default);

    Task<ExpenseResponse> UpdateAsync(string userUuid, string expenseUuid, UpdateExpenseRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);

    Task SetSettledAsync(string userUuid, string expenseUuid, SetSettledRequest request, CancellationToken cancellationToken = default);

    Task<ExpenseResponse> AssignEventAsync(string userUuid, string expenseUuid, AssignEventRequest request, CancellationToken cancellationToken = default);

    Task RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLogResponse>> GetHistoryAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IExpensesService))]
public sealed class ExpensesService(
    IExpenseRepository expenseRepository,
    IAuditLogRepository auditLogRepository,
    ITierService tierService,
    IMapper mapper,
    IValidator<CreateExpenseRequest> createValidator,
    IValidator<UpdateExpenseRequest> updateValidator,
    IValidator<AssignEventRequest> assignEventValidator) : IExpensesService
{
    public async Task<IReadOnlyList<ExpenseSummaryResponse>> ListAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default)
    {
        var expenses = await expenseRepository.ListByUserAsync(userUuid, filter, cancellationToken);
        return mapper.Map<IReadOnlyList<ExpenseSummaryResponse>>(expenses);
    }

    public async Task<ExpenseResponse> GetAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
    {
        var expense = await expenseRepository.GetByUuidAsync(userUuid, expenseUuid, cancellationToken)
            ?? throw ExpenseNotFound();

        return mapper.Map<ExpenseResponse>(expense);
    }

    public async Task<ExpenseResponse> CreateAsync(string userUuid, CreateExpenseRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        // M10 Free tier limit (create-only): counts this user's expenses in the current +7 calendar month.
        await tierService.EnsureCanCreateExpenseAsync(userUuid, cancellationToken);

        var data = new CreateExpenseData(
            request.Name.Trim(),
            request.Description?.Trim(),
            request.ExpenseTime,
            request.PayerMemberUuid,
            request.CategoryUuid,
            request.TagUuids ?? [],
            (request.Shares ?? []).Select(share => new CreateShareData(share.MemberUuid, share.Amount, share.Note?.Trim())).ToList(),
            request.EventUuid?.Trim());

        var result = await expenseRepository.CreateAsync(userUuid, data, cancellationToken);
        ThrowIfFailed(result.Status);

        return await LoadResponseAsync(userUuid, result.Entity!.Uuid, cancellationToken);
    }

    public async Task<ExpenseResponse> UpdateAsync(string userUuid, string expenseUuid, UpdateExpenseRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var data = new UpdateExpenseData(
            request.Name.Trim(),
            request.Description?.Trim(),
            request.ExpenseTime,
            request.PayerMemberUuid,
            request.CategoryUuid,
            request.TagUuids ?? []);

        var result = await expenseRepository.UpdateGeneralInfoAsync(userUuid, expenseUuid, data, cancellationToken);
        ThrowIfFailed(result.Status);

        return await LoadResponseAsync(userUuid, expenseUuid, cancellationToken);
    }

    public async Task DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
    {
        var status = await expenseRepository.DeleteAsync(userUuid, expenseUuid, cancellationToken);
        ThrowIfFailed(status);
    }

    public async Task SetSettledAsync(string userUuid, string expenseUuid, SetSettledRequest request, CancellationToken cancellationToken = default)
    {
        var status = await expenseRepository.SetSettledAsync(userUuid, expenseUuid, request.IsSettled, cancellationToken);
        if (status != ExpenseWriteStatus.Success)
            throw ExpenseNotFound();
    }

    public async Task<ExpenseResponse> AssignEventAsync(string userUuid, string expenseUuid, AssignEventRequest request, CancellationToken cancellationToken = default)
    {
        await assignEventValidator.ValidateAndThrowAsync(request, cancellationToken);

        var result = await expenseRepository.AssignEventAsync(userUuid, expenseUuid, request.EventUuid.Trim(), cancellationToken);
        ThrowIfFailed(result.Status);

        return await LoadResponseAsync(userUuid, expenseUuid, cancellationToken);
    }

    public async Task RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
    {
        var status = await expenseRepository.RemoveEventAsync(userUuid, expenseUuid, cancellationToken);

        switch (status)
        {
            case ExpenseWriteStatus.Success:
                return;
            case ExpenseWriteStatus.EventClosed:
                throw new ErrorException(ErrorCodes.EventClosed, "Không thể gỡ phiếu khỏi đợt đã chốt.");
            default:
                throw ExpenseNotFound();
        }
    }

    public async Task<IReadOnlyList<AuditLogResponse>> GetHistoryAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
    {
        var logs = await auditLogRepository.ListByExpenseAsync(userUuid, expenseUuid, cancellationToken);
        return mapper.Map<IReadOnlyList<AuditLogResponse>>(logs);
    }

    private async Task<ExpenseResponse> LoadResponseAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken)
    {
        var expense = await expenseRepository.GetByUuidAsync(userUuid, expenseUuid, cancellationToken)
            ?? throw ExpenseNotFound();

        return mapper.Map<ExpenseResponse>(expense);
    }

    private static void ThrowIfFailed(ExpenseWriteStatus status)
    {
        switch (status)
        {
            case ExpenseWriteStatus.Success:
                return;
            case ExpenseWriteStatus.PayerInvalid:
                throw new ErrorException(ErrorCodes.ExpensePayerInvalid, "Người trả không hợp lệ hoặc đã bị xóa.");
            case ExpenseWriteStatus.CategoryInvalid:
                throw new ErrorException(ErrorCodes.ExpenseCategoryInvalid, "Danh mục không hợp lệ hoặc đã bị xóa.");
            case ExpenseWriteStatus.TagInvalid:
                throw new ErrorException(ErrorCodes.ExpenseTagInvalid, "Nhãn không hợp lệ hoặc đã bị xóa.");
            case ExpenseWriteStatus.ShareMemberInvalid:
                throw new ErrorException(ErrorCodes.ShareMemberInvalid, "Thành viên của phần gánh không hợp lệ hoặc đã bị xóa.");
            case ExpenseWriteStatus.DuplicateShareMember:
                throw new ErrorException(ErrorCodes.DuplicateShareMember, "Mỗi thành viên chỉ có một phần gánh trong một phiếu.");
            case ExpenseWriteStatus.EventNotFound:
                throw new ErrorException(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
            case ExpenseWriteStatus.EventClosed:
                throw new ErrorException(ErrorCodes.EventClosed, "Đợt chi tiêu đã chốt, không thể thay đổi.");
            case ExpenseWriteStatus.ExpenseTimeOutOfEventRange:
                throw new ErrorException(ErrorCodes.ExpenseTimeOutOfEventRange, "Thời điểm chi của phiếu không nằm trong khoảng thời gian của đợt.");
            default:
                throw ExpenseNotFound();
        }
    }

    private static ErrorException ExpenseNotFound() =>
        new(ErrorCodes.ExpenseNotFound, "Không tìm thấy phiếu chi tiêu.");
}
