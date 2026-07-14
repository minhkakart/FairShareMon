using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Stats;

/// <summary>
/// Read-only business logic for the per-event debt balance (§3.7) and the overview / by-category
/// statistics (§3.9) - M7. All three reads are resource-owned: an event ownership miss maps to
/// <c>EventNotFound</c> 9000 (404, never 403). The balance ignores <c>is_settled</c> (OQ2), covers both
/// OPEN and CLOSED events (OQ4), and returns empty rows for an owned-but-empty event (OQ15). Ranges are
/// validated (from ≤ to; by-category rejects a range together with an event) before hitting the
/// DB-side aggregation in <see cref="IStatsRepository"/>.
/// </summary>
public interface IStatsService
{
    Task<EventBalanceResponse> GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    Task<OverviewStatsResponse> GetOverviewAsync(string userUuid, StatsRangeRequest range, CancellationToken cancellationToken = default);

    Task<ByCategoryStatsResponse> GetByCategoryAsync(string userUuid, ByCategoryStatsRequest request, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IStatsService))]
public sealed class StatsService(
    IStatsRepository statsRepository,
    IMapper mapper,
    IValidator<StatsRangeRequest> rangeValidator,
    IValidator<ByCategoryStatsRequest> byCategoryValidator) : IStatsService
{
    public async Task<EventBalanceResponse> GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
    {
        var evt = await statsRepository.FindOwnedEventAsync(userUuid, eventUuid, cancellationToken)
            ?? throw EventNotFound();

        var aggregates = await statsRepository.GetEventBalanceAsync(userUuid, evt.Id, cancellationToken);

        return new EventBalanceResponse
        {
            EventUuid = evt.Uuid,
            EventName = evt.Name,
            IsClosed = evt.IsClosed,
            Rows = mapper.Map<IReadOnlyList<MemberBalanceRow>>(aggregates)
        };
    }

    public async Task<OverviewStatsResponse> GetOverviewAsync(string userUuid, StatsRangeRequest range, CancellationToken cancellationToken = default)
    {
        await rangeValidator.ValidateAndThrowAsync(range, cancellationToken);

        var aggregate = await statsRepository.GetOverviewAsync(userUuid, range.From, range.To, cancellationToken);

        var response = mapper.Map<OverviewStatsResponse>(aggregate);
        response.From = range.From;
        response.To = range.To;
        return response;
    }

    public async Task<ByCategoryStatsResponse> GetByCategoryAsync(string userUuid, ByCategoryStatsRequest request, CancellationToken cancellationToken = default)
    {
        await byCategoryValidator.ValidateAndThrowAsync(request, cancellationToken);

        ulong? eventId = null;
        if (!string.IsNullOrEmpty(request.EventUuid))
        {
            var evt = await statsRepository.FindOwnedEventAsync(userUuid, request.EventUuid, cancellationToken)
                ?? throw EventNotFound();
            eventId = evt.Id;
        }

        var aggregates = await statsRepository.GetByCategoryAsync(
            userUuid, request.From, request.To, eventId, cancellationToken);

        return new ByCategoryStatsResponse
        {
            EventUuid = request.EventUuid,
            From = string.IsNullOrEmpty(request.EventUuid) ? request.From : null,
            To = string.IsNullOrEmpty(request.EventUuid) ? request.To : null,
            Rows = mapper.Map<IReadOnlyList<CategoryStatRow>>(aggregates)
        };
    }

    private static ErrorException EventNotFound() =>
        new(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
}
