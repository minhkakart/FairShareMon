using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Services.Registration;

namespace FairShareMonApi.Services.Api.Categories;

/// <summary>
/// Registration bootstrap step (OQ3): seeds the suggested-category set (one <c>IsDefault = true</c>,
/// OQ1) in the same transaction as the user insert, so a registration atomically creates the
/// owner-representative member AND the suggested categories - a rollback leaves none of them.
/// Registered with <c>Multiple = true</c> so it runs alongside <c>OwnerRepresentativeBootstrapStep</c>
/// on the shared M3 seam; <c>AuthService</c> already iterates all steps.
/// </summary>
[ScopedService(typeof(IRegistrationBootstrapStep), Multiple = true)]
public sealed class SuggestedCategoriesBootstrapStep : IRegistrationBootstrapStep
{
    public Task RunAsync(AppDbContext dbContext, User user, CancellationToken cancellationToken)
    {
        dbContext.Categories.AddRange(Category.BuildSuggestedSet(user.Id));
        return Task.CompletedTask;
    }
}
