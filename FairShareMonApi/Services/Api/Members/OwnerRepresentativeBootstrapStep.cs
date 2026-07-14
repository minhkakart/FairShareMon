using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Services.Registration;

namespace FairShareMonApi.Services.Api.Members;

/// <summary>
/// Registration bootstrap step (OQ1): adds the ledger's single owner-representative member, named
/// "Tôi" (OQ5), in the same transaction as the user insert. Registered with <c>Multiple = true</c>
/// so Milestone 4 can add a suggested-category step alongside it on the same seam.
/// </summary>
[ScopedService(typeof(IRegistrationBootstrapStep), Multiple = true)]
public sealed class OwnerRepresentativeBootstrapStep : IRegistrationBootstrapStep
{
    public Task RunAsync(AppDbContext dbContext, User user, CancellationToken cancellationToken)
    {
        dbContext.Members.Add(new Member
        {
            UserId = user.Id,
            Name = Member.OwnerRepresentativeDefaultName,
            IsOwnerRepresentative = true
        });
        return Task.CompletedTask;
    }
}
