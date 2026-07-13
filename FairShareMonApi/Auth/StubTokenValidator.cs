using DiDecoration.Attributes;
using FairShareMonApi.Auth.Abstractions;

namespace FairShareMonApi.Auth;

/// <summary>
/// PLACEHOLDER - authenticates nothing: every bearer token is rejected, so all [Authorize]
/// requests return 401 until the real auth feature lands. No logic, no exceptions.
/// The auth feature MUST DELETE this file, not just add a real implementation: DiDecoration
/// registers with TryAdd, so a leftover stub registration silently wins over the real one.
/// </summary>
[ScopedService(typeof(ITokenValidator))]
public sealed class StubTokenValidator : ITokenValidator
{
    public Task<AuthenticatedUser?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthenticatedUser?>(null);
}
