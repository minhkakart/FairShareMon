using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Auth;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Auth;
using FairShareMonApi.Services.Api.Members;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for the registration-bootstrap seam and the owner-representative backfill,
/// resolved from the application's own DI container (real repositories/services + MariaDB;
/// skippable when the DB is unreachable). Proves atomicity (a rolled-back registration leaves
/// neither a user nor a member), the "Tôi" owner-rep created exactly once on register, and the
/// idempotent backfill.
/// </summary>
[Collection("AuthIntegration")]
public class MemberBootstrapTests(WebApplicationFactory<Program> factory, DatabaseFixture fixture)
    : AuthApiTestBase(factory, fixture), IClassFixture<WebApplicationFactory<Program>>, IClassFixture<DatabaseFixture>
{
    private static User NewUser(string username) => new() { Username = username, PasswordHash = "seeded-hash" };

    private static async Task<List<Member>> MembersOfAsync(IServiceScope scope, string userUuid) =>
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Members
            .AsNoTracking()
            .Where(member => member.User.Uuid == userUuid)
            .ToListAsync();

    [SkippableFact]
    public async Task Register_NewUser_CreatesExactlyOneOwnerRepMemberNamedToi()
    {
        using var scope = CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var user = await authService.RegisterAsync(new RegisterRequest { Username = NewUsername(), Password = "password-8+" });

        var members = await MembersOfAsync(scope, user.Uuid);
        var member = Assert.Single(members); // exactly one - atomic bootstrap, no duplicates
        Assert.True(member.IsOwnerRepresentative);
        Assert.Equal(Member.OwnerRepresentativeDefaultName, member.Name); // "Tôi" (OQ5)
        Assert.False(member.IsDeleted);
    }

    [SkippableFact]
    public async Task CreateWithBootstrap_BootstrapThrows_RollsBackUserAndMember()
    {
        using var scope = CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var username = NewUsername();

        // Forced failure INSIDE the bootstrap (which runs in the user-creation transaction).
        await Assert.ThrowsAnyAsync<Exception>(() => userRepository.CreateWithBootstrapAsync(
            NewUser(username),
            (db, user, _) =>
            {
                db.Members.Add(new Member { UserId = user.Id, Name = "Tôi", IsOwnerRepresentative = true });
                throw new InvalidOperationException("forced bootstrap failure");
            }));

        // Atomic: neither the user (already flushed to assign its Id) nor the member survives.
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.Users.AsNoTracking().AnyAsync(user => user.Username == username));
        Assert.False(await context.Members.AsNoTracking().AnyAsync(member => member.Name == "Tôi" && member.User.Username == username));
    }

    [SkippableFact]
    public async Task CreateWithBootstrap_Success_PersistsUserAndMemberAtomically()
    {
        using var scope = CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var username = NewUsername();

        var created = await userRepository.CreateWithBootstrapAsync(
            NewUser(username),
            (db, user, _) =>
            {
                db.Members.Add(new Member { UserId = user.Id, Name = Member.OwnerRepresentativeDefaultName, IsOwnerRepresentative = true });
                return Task.CompletedTask;
            });

        Assert.NotNull(created);
        var members = await MembersOfAsync(scope, created!.Uuid);
        Assert.Single(members);
        Assert.True(members[0].IsOwnerRepresentative);
    }

    [SkippableFact]
    public async Task Backfill_UserWithoutOwnerRep_GetsExactlyOneAndIsIdempotent()
    {
        using var scope = CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var membersService = scope.ServiceProvider.GetRequiredService<IMembersService>();

        // A user created WITHOUT the bootstrap (models a pre-M3 registration).
        var user = await userRepository.CreateAsync(NewUser(NewUsername()));
        Assert.NotNull(user);
        Assert.Empty(await MembersOfAsync(scope, user!.Uuid));

        await membersService.EnsureOwnerRepresentativeForAllAsync();
        var afterFirst = await MembersOfAsync(scope, user.Uuid);

        await membersService.EnsureOwnerRepresentativeForAllAsync(); // running again must not duplicate
        var afterSecond = await MembersOfAsync(scope, user.Uuid);

        var member = Assert.Single(afterFirst);
        Assert.True(member.IsOwnerRepresentative);
        Assert.Equal(Member.OwnerRepresentativeDefaultName, member.Name);
        Assert.Single(afterSecond); // idempotent - still exactly one
        Assert.Equal(member.Uuid, afterSecond[0].Uuid);
    }

    [SkippableFact]
    public async Task Backfill_UserThatAlreadyHasOwnerRep_IsUntouched()
    {
        using var scope = CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var membersService = scope.ServiceProvider.GetRequiredService<IMembersService>();

        // Registered normally -> already has its owner-rep member.
        var user = await authService.RegisterAsync(new RegisterRequest { Username = NewUsername(), Password = "password-8+" });
        var ownerRepUuidBefore = (await MembersOfAsync(scope, user.Uuid)).Single().Uuid;

        await membersService.EnsureOwnerRepresentativeForAllAsync();

        var member = Assert.Single(await MembersOfAsync(scope, user.Uuid));
        Assert.Equal(ownerRepUuidBefore, member.Uuid); // same row, no second owner-rep created
    }
}
