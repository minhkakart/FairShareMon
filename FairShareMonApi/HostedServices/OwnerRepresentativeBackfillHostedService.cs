using FairShareMonApi.Services.Api.Members;

namespace FairShareMonApi.HostedServices;

/// <summary>
/// Idempotent startup backfill (OQ2): on boot, gives every user that lacks an active
/// owner-representative member one, and is a no-op when none are missing. Closes the Milestone 2
/// deferral for users registered before Milestone 3 shipped; self-heals on every boot. Runs in its
/// own DI scope and never crashes startup - a failure (e.g. DB unreachable) is logged and the boot
/// continues (the next boot retries).
/// </summary>
public sealed class OwnerRepresentativeBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OwnerRepresentativeBackfillHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var membersService = scope.ServiceProvider.GetRequiredService<IMembersService>();

            var created = await membersService.EnsureOwnerRepresentativeForAllAsync(cancellationToken);
            if (created > 0)
                logger.LogInformation("Owner-representative backfill created {Count} member(s).", created);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Owner-representative backfill failed on startup; will retry on next boot.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
