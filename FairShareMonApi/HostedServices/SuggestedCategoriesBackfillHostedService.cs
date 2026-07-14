using DiDecoration.Attributes;
using FairShareMonApi.Services.Api.Categories;

namespace FairShareMonApi.HostedServices;

/// <summary>
/// Idempotent startup backfill (OQ3): on boot, gives every user that lacks an active default category
/// the suggested set (or elects a default), and is a no-op when none are missing. Closes the
/// Milestone 3 suggested-category deferral for users registered before Milestone 4 shipped;
/// self-heals on every boot. Mirrors <see cref="OwnerRepresentativeBackfillHostedService"/>: runs in
/// its own DI scope and never crashes startup - a failure (e.g. DB unreachable) is logged and the
/// boot continues (the next boot retries).
/// </summary>
[BackgroundService]
public sealed class SuggestedCategoriesBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SuggestedCategoriesBackfillHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var categoriesService = scope.ServiceProvider.GetRequiredService<ICategoriesService>();

            var created = await categoriesService.EnsureSuggestedCategoriesForAllAsync(stoppingToken);
            if (created > 0)
                logger.LogInformation("Suggested-category backfill seeded/fixed {Count} user(s).", created);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Suggested-category backfill failed on startup; will retry on next boot.");
        }
    }
}
