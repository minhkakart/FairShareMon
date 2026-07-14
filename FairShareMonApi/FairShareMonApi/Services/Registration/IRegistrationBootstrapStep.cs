using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;

namespace FairShareMonApi.Services.Registration;

/// <summary>
/// A unit of ledger bootstrap that runs when a user registers, INSIDE the same transaction as the
/// <c>users</c> insert (after <c>SaveChanges</c> has assigned <see cref="User.Id"/>), so the whole
/// registration is atomic - a rollback leaves neither the user nor any bootstrapped data.
/// Implementations register with <c>[ScopedService(typeof(IRegistrationBootstrapStep), Multiple = true)]</c>;
/// <c>IUserRepository.CreateWithBootstrapAsync</c> runs every registered step. Milestone 3 adds the
/// owner-representative member; Milestone 4 will add suggested categories on this same seam.
/// </summary>
public interface IRegistrationBootstrapStep
{
    /// <summary>Adds this step's rows to the tracked context. Do NOT call <c>SaveChanges</c>/commit - the caller owns the transaction.</summary>
    Task RunAsync(AppDbContext dbContext, User user, CancellationToken cancellationToken);
}
