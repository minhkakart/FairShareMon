using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Auth;
using FairShareMonApi.Services.Api.Categories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for the suggested-category registration-bootstrap step and the idempotent
/// backfill, resolved from the application's own DI container (real repositories/services + MariaDB;
/// skippable). Proves the five-with-one-default seed is created atomically alongside the owner-rep
/// member on register, a rolled-back registration leaves neither user nor categories, and the backfill
/// self-heals a category-less user exactly once. Backfill assertions are per-seeded-user, never on the
/// global return count (the backfill scans all users).
/// </summary>
[Collection("AuthIntegration")]
public class CategoryBootstrapTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    private static User NewUser(string username) => new() { Username = username, PasswordHash = "seeded-hash" };

    private static async Task<List<Category>> CategoriesOfAsync(IServiceScope scope, string userUuid) =>
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Categories
            .AsNoTracking()
            .Where(category => category.User.Uuid == userUuid)
            .ToListAsync();

    [SkippableFact]
    public async Task Register_NewUser_SeedsTheFiveSuggestedCategoriesWithExactlyOneDefaultAnUong()
    {
        using var scope = CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var user = await authService.RegisterAsync(new RegisterRequest { Username = NewUsername(), Password = "password-8+" });

        var categories = await CategoriesOfAsync(scope, user.Uuid);
        Assert.Equal(5, categories.Count);
        Assert.Equal(
            new[] { "Ăn uống", "Đi lại", "Khách sạn", "Mua sắm", "Khác" }.OrderBy(name => name),
            categories.Select(category => category.Name).OrderBy(name => name));
        var defaultCategory = Assert.Single(categories, category => category.IsDefault); // exactly one default
        Assert.Equal("Ăn uống", defaultCategory.Name); // OQ1(b)
        Assert.Equal("🍜", defaultCategory.Icon);
        Assert.Equal("#F97316", defaultCategory.Color);
        Assert.All(categories, category => Assert.False(category.IsDeleted));
    }

    [SkippableFact]
    public async Task Register_NewUser_CreatesCategoriesAtomicallyAlongsideTheOwnerRepMember()
    {
        using var scope = CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var user = await authService.RegisterAsync(new RegisterRequest { Username = NewUsername(), Password = "password-8+" });

        // Both bootstrap steps ran in the same registration transaction.
        var members = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Members
            .AsNoTracking().Where(member => member.User.Uuid == user.Uuid).ToListAsync();
        Assert.Single(members);
        Assert.True(members[0].IsOwnerRepresentative);
        Assert.Equal(5, (await CategoriesOfAsync(scope, user.Uuid)).Count);
    }

    [SkippableFact]
    public async Task CreateWithBootstrap_BootstrapThrows_RollsBackUserAndCategories()
    {
        using var scope = CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var username = NewUsername();

        // Forced failure INSIDE the bootstrap after staging categories (runs in the user-creation txn).
        await Assert.ThrowsAnyAsync<Exception>(() => userRepository.CreateWithBootstrapAsync(
            NewUser(username),
            (db, user, _) =>
            {
                db.Categories.AddRange(Category.BuildSuggestedSet(user.Id));
                throw new InvalidOperationException("forced bootstrap failure");
            }));

        // Atomic: neither the user (already flushed to assign its Id) nor its categories survive.
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.Users.AsNoTracking().AnyAsync(user => user.Username == username));
        Assert.False(await context.Categories.AsNoTracking().AnyAsync(category => category.User.Username == username));
    }

    [SkippableFact]
    public async Task Backfill_CategoryLessUser_GetsTheFiveWithOneDefaultAndIsIdempotent()
    {
        using var scope = CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var categoriesService = scope.ServiceProvider.GetRequiredService<ICategoriesService>();

        // A user created WITHOUT the bootstrap (models a pre-M4 registration).
        var user = await userRepository.CreateAsync(NewUser(NewUsername()));
        Assert.NotNull(user);
        Assert.Empty(await CategoriesOfAsync(scope, user!.Uuid));

        await categoriesService.EnsureSuggestedCategoriesForAllAsync();
        var afterFirst = await CategoriesOfAsync(scope, user.Uuid);

        await categoriesService.EnsureSuggestedCategoriesForAllAsync(); // running again must not duplicate
        var afterSecond = await CategoriesOfAsync(scope, user.Uuid);

        Assert.Equal(5, afterFirst.Count);
        Assert.Equal("Ăn uống", Assert.Single(afterFirst, category => category.IsDefault).Name);
        Assert.Equal(5, afterSecond.Count); // idempotent - still exactly five
        Assert.Single(afterSecond, category => category.IsDefault); // still exactly one default
    }

    [SkippableFact]
    public async Task Backfill_UserThatAlreadyHasSuggestedCategories_IsUntouched()
    {
        using var scope = CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var categoriesService = scope.ServiceProvider.GetRequiredService<ICategoriesService>();

        // Registered normally -> already has its five categories and one default.
        var user = await authService.RegisterAsync(new RegisterRequest { Username = NewUsername(), Password = "password-8+" });
        var defaultUuidBefore = (await CategoriesOfAsync(scope, user.Uuid)).Single(category => category.IsDefault).Uuid;

        await categoriesService.EnsureSuggestedCategoriesForAllAsync();

        var after = await CategoriesOfAsync(scope, user.Uuid);
        Assert.Equal(5, after.Count); // no second copy seeded
        Assert.Equal(defaultUuidBefore, after.Single(category => category.IsDefault).Uuid); // same default row
    }
}
