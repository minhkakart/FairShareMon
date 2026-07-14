using DiDecoration.Attributes;
using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;

namespace FairShareMonApi.HostedServices;

/// <summary>
/// Idempotent startup seeder for the config-driven admin account (M11, OQ9), mirroring
/// <see cref="OwnerRepresentativeBackfillHostedService"/>: reads <c>Admin:Username</c> /
/// <c>Admin:Password</c>; if either is absent it logs a warning and no-ops (safe default - no admin
/// seeded); otherwise it creates the account (Free tier, ADMIN role, ACTIVE, BCrypt-hashed password)
/// when missing, or promotes an existing account to ADMIN <b>without overwriting its password</b>.
/// Runs in its own DI scope and never crashes boot - a failure (e.g. DB unreachable) is logged and the
/// boot continues (the next boot retries). <b>The password is never logged.</b>
/// </summary>
[BackgroundService]
public sealed class AdminSeederHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminSeederHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredUsername = configuration.GetValue<string>("Admin:Username");
        var configuredPassword = configuration.GetValue<string>("Admin:Password");

        if (string.IsNullOrWhiteSpace(configuredUsername) || string.IsNullOrWhiteSpace(configuredPassword))
        {
            logger.LogWarning("Admin seeder skipped: Admin:Username and/or Admin:Password are not configured.");
            return;
        }

        var username = configuredUsername.Trim().ToLowerInvariant();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var existing = await userRepository.GetByUsernameAsync(username, stoppingToken);
            if (existing is null)
            {
                var admin = new User
                {
                    Username = username,
                    PasswordHash = passwordHasher.Hash(configuredPassword),
                    Role = UserRoles.Admin,
                    Status = UserStatuses.Active
                };

                var created = await userRepository.CreateAsync(admin, stoppingToken);
                if (created is null)
                    logger.LogWarning("Admin seeder: account '{Username}' was created concurrently; skipping.", username);
                else
                    logger.LogInformation("Admin seeder created the admin account '{Username}'.", username);

                return;
            }

            if (existing.Role != UserRoles.Admin)
            {
                await userRepository.SetRoleAsync(existing.Uuid, UserRoles.Admin, stoppingToken);
                logger.LogInformation("Admin seeder promoted existing account '{Username}' to ADMIN.", username);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin seeder failed on startup; will retry on next boot.");
        }
    }
}
