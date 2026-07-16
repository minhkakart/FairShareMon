using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Tiers;
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
    ITierService tierService,
    IRequestTimeZone requestTimeZone,
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

        // M10 Free tier limit (create-only): only OPEN events count, so closing one frees a slot.
        await tierService.EnsureCanCreateOpenEventAsync(userUuid, cancellationToken);

        var data = new CreateEventData(request.Name.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate, requestTimeZone.Zone);
        var result = await eventRepository.CreateAsync(userUuid, data, cancellationToken);
        ThrowIfFailed(result.Status);

        return await LoadResponseAsync(userUuid, result.Entity!.Uuid, cancellationToken);
    }

    public async Task<EventResponse> UpdateAsync(string userUuid, string eventUuid, UpdateEventRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var data = new UpdateEventData(request.Name.Trim(), request.Description?.Trim(), request.StartDate, request.EndDate, requestTimeZone.Zone);
        var result = await eventRepository.UpdateAsync(userUuid, eventUuid, data, cancellationToken);
        ThrowIfFailed(result.Status, MessageKeys.Error.EventClosedEdit);

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
        ThrowIfFailed(status, MessageKeys.Error.EventClosedDelete);
    }

    private async Task<EventResponse> LoadResponseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken)
    {
        var evt = await eventRepository.GetByUuidAsync(userUuid, eventUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<EventResponse>(evt);
    }

    private static void ThrowIfFailed(EventWriteStatus status, string? closedMessageKey = null)
    {
        switch (status)
        {
            case EventWriteStatus.Success:
                return;
            case EventWriteStatus.EventClosed:
                throw new ErrorException(ErrorCodes.EventClosed, closedMessageKey ?? MessageKeys.Error.EventClosed);
            case EventWriteStatus.RangeExcludesAssignedExpenses:
                throw new ErrorException(ErrorCodes.EventRangeExcludesAssignedExpenses,
                    MessageKeys.Error.EventRangeExcludesAssignedExpenses);
            default:
                throw NotFound();
        }
    }

    private static ErrorException NotFound() =>
        new(ErrorCodes.EventNotFound, MessageKeys.Error.EventNotFound);
}
