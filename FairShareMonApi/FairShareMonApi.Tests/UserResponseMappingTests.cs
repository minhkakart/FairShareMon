using System.Text.Json;
using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Auth;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for the "expose current-user profile + role" feature:
/// <c>AuthProfile</c>'s <c>User -&gt; UserResponse</c> mapping now projects <c>Role</c> (the additive
/// field this feature adds) alongside the existing <c>Uuid</c>/<c>Username</c>/<c>Tier</c>/<c>CreatedAt</c>,
/// AutoMapper's configuration assertion still passes with the new member (no unmapped destination
/// member), and the DTO serializes <c>role</c> (and <c>tier</c>) as camelCase JSON keys the SPA branches
/// on - with no password/hash key ever present. Mirrors <c>AuthenticatedUserRoleTests</c>' fail-safe
/// framing at the mapping/serialization layer.
/// </summary>
public class UserResponseMappingTests
{
    private static readonly IMapper Mapper =
        new MapperConfiguration(config => config.AddProfile<AuthProfile>()).CreateMapper();

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static User SampleUser(string role) => new()
    {
        Id = 42,
        Uuid = "0198a5c2-0000-7000-8000-00000000e001",
        Username = "an",
        PasswordHash = "$2a$11$super-secret-bcrypt-hash-value-never-leaks",
        Tier = UserTiers.Premium,
        Role = role,
        Status = UserStatuses.Active,
        CreatedAt = new DateTime(2026, 7, 16, 10, 30, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 16, 11, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void MapperConfiguration_WithRoleMember_IsValid()
    {
        // The new Role member on UserResponse must have a matching source (User.Role) or AutoMapper's
        // assertion fails - this locks the "no profile change needed" claim (OQ1a).
        var configuration = new MapperConfiguration(config => config.AddProfile<AuthProfile>());

        configuration.AssertConfigurationIsValid();
    }

    [Theory]
    [InlineData(UserRoles.User)]
    [InlineData(UserRoles.Admin)]
    public void Map_UserToUserResponse_ProjectsRoleAndEveryOtherField(string role)
    {
        var user = SampleUser(role);

        var response = Mapper.Map<UserResponse>(user);

        Assert.Equal(role, response.Role);           // the additive field this feature adds
        Assert.Equal(UserTiers.Premium, response.Tier);
        Assert.Equal(user.Uuid, response.Uuid);
        Assert.Equal(user.Username, response.Username);
        Assert.Equal(user.CreatedAt, response.CreatedAt); // CreatedAt is why a live DB read is required (OQ3a)
    }

    [Fact]
    public void Serialize_UserResponse_EmitsCamelCaseRoleAndTierKeys()
    {
        var response = Mapper.Map<UserResponse>(SampleUser(UserRoles.Admin));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, WebJson));
        var root = document.RootElement;

        // camelCase contract the SPA's AdminRoute guard and account label branch on.
        Assert.Equal(UserRoles.Admin, root.GetProperty("role").GetString());
        Assert.Equal(UserTiers.Premium, root.GetProperty("tier").GetString());
        Assert.Equal("an", root.GetProperty("username").GetString());
        Assert.Equal("0198a5c2-0000-7000-8000-00000000e001", root.GetProperty("uuid").GetString());
        Assert.True(root.TryGetProperty("createdAt", out _));

        // The DTO's documented invariant: no secret material is ever serialized.
        Assert.False(root.TryGetProperty("password", out _));
        Assert.False(root.TryGetProperty("passwordHash", out _));
    }
}
