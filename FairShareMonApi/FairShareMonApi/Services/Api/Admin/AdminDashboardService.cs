using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Admin;

/// <summary>
/// Read-only admin dashboards (M11). Metrics aggregate ACCOUNT METADATA over <c>users</c> only (OQ6);
/// revenue aggregates GRANT rows over <c>tier_grants</c> only (OQ14). <b>Neither touches any ledger
/// table - not even an anonymous aggregate (R10).</b>
/// </summary>
public interface IAdminDashboardService
{
    Task<AdminMetricsResponse> GetMetricsAsync(AdminMetricsRequest request, CancellationToken cancellationToken = default);

    Task<RevenueResponse> GetRevenueAsync(RevenueRequest request, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IAdminDashboardService))]
public sealed class AdminDashboardService(
    IAdminDashboardRepository dashboardRepository,
    ITierGrantRepository tierGrantRepository,
    IMapper mapper,
    IValidator<AdminMetricsRequest> metricsValidator,
    IValidator<RevenueRequest> revenueValidator) : IAdminDashboardService
{
    public async Task<AdminMetricsResponse> GetMetricsAsync(AdminMetricsRequest request, CancellationToken cancellationToken = default)
    {
        await metricsValidator.ValidateAndThrowAsync(request, cancellationToken);

        var aggregate = await dashboardRepository.GetMetricsAsync(request.From, request.To, request.Bucket, cancellationToken);

        var response = mapper.Map<AdminMetricsResponse>(aggregate);
        response.From = request.From;
        response.To = request.To;
        return response;
    }

    public async Task<RevenueResponse> GetRevenueAsync(RevenueRequest request, CancellationToken cancellationToken = default)
    {
        await revenueValidator.ValidateAndThrowAsync(request, cancellationToken);

        var aggregate = await tierGrantRepository.GetRevenueAsync(request.From, request.To, request.Bucket, cancellationToken);

        var response = mapper.Map<RevenueResponse>(aggregate);
        response.From = request.From;
        response.To = request.To;
        response.Bucket = request.Bucket;
        return response;
    }
}
