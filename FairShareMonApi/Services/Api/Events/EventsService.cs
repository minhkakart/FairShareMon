using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Events;

/// <summary>
/// Business logic for The-ideal.md §3.6 (Đợt chi tiêu): list / get / create / update-info / close
/// (one-way) / delete, all resource-owned (an ownership miss -&gt; <c>EventNotFound</c> 404, never
/// 403). Maps the repository's typed <see cref="EventWriteStatus"/> to the right 9xxx
/// <c>ErrorException</c>. Editing/deleting/re-closing a closed event is rejected (9001); an
/// info edit whose new range would exclude an already-assigned expense is rejected (9003, OQ7).
/// </summary>
public interface IEventsService
{
    Task<IReadOnlyList<EventSummaryResponse>> ListAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default);

    Task<EventResponse> GetAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    Task<EventResponse> CreateAsync(string userUuid, CreateEventRequest request, CancellationToken cancellationToken = default);

    Task<EventResponse> UpdateAsync(string userUuid, string eventUuid, UpdateEventRequest request, CancellationToken cancellationToken = default);

    Task CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IEventsService))]
public sealed class EventsService(
    IEventRepository eventRepository,
    IMapper mapper,
    IValidator<CreateEventRequest> createValidator,
    IValidator<UpdateEventRequest> updateValidator) : IEventsService
{
    public async Task<IReadOnlyList<EventSummaryResponse>> ListAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default)
    {
        var events = await eventRepository.ListByUserAsync(userUuid, filter, cancellationToken);
        return mapper.Map<IReadOnlyList<EventSummaryResponse>>(events);
    }

    public async Task<EventResponse> GetAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
    {
        var evt = await eventRepository.GetByUuidAsync(userUuid, eventUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<EventResponse>(evt);
    }

    public async Task<EventResponse> CreateAsync(string userUuid, CreateEventRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var data = new CreateEventData(request.Name.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate);
        var result = await eventRepository.CreateAsync(userUuid, data, cancellationToken);
        ThrowIfFailed(result.Status);

        return await LoadResponseAsync(userUuid, result.Entity!.Uuid, cancellationToken);
    }

    public async Task<EventResponse> UpdateAsync(string userUuid, string eventUuid, UpdateEventRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var data = new UpdateEventData(request.Name.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate);
        var result = await eventRepository.UpdateAsync(userUuid, eventUuid, data, cancellationToken);
        ThrowIfFailed(result.Status, "Không thể sửa đợt đã chốt.");

        return await LoadResponseAsync(userUuid, eventUuid, cancellationToken);
    }

    public async Task CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
    {
        var status = await eventRepository.CloseAsync(userUuid, eventUuid, cancellationToken);
        ThrowIfFailed(status);
    }

    public async Task DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
    {
        var status = await eventRepository.DeleteAsync(userUuid, eventUuid, cancellationToken);
        ThrowIfFailed(status, "Không thể xóa đợt đã chốt.");
    }

    private async Task<EventResponse> LoadResponseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken)
    {
        var evt = await eventRepository.GetByUuidAsync(userUuid, eventUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<EventResponse>(evt);
    }

    private static void ThrowIfFailed(EventWriteStatus status, string? closedMessage = null)
    {
        switch (status)
        {
            case EventWriteStatus.Success:
                return;
            case EventWriteStatus.EventClosed:
                throw new ErrorException(ErrorCodes.EventClosed, closedMessage ?? "Đợt chi tiêu đã chốt, không thể thay đổi.");
            case EventWriteStatus.RangeExcludesAssignedExpenses:
                throw new ErrorException(ErrorCodes.EventRangeExcludesAssignedExpenses,
                    "Không thể đổi khoảng thời gian: có phiếu đã gán nằm ngoài khoảng thời gian mới.");
            default:
                throw NotFound();
        }
    }

    private static ErrorException NotFound() =>
        new(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
}
