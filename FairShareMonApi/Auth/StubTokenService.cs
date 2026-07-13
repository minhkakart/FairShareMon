using DiDecoration.Attributes;
using FairShareMonApi.Auth.Abstractions;

namespace FairShareMonApi.Auth;

/// <summary>
/// PLACEHOLDER - every operation reports failure (null/false/0). No logic, no exceptions.
/// The auth feature MUST DELETE this file, not just add a real implementation: DiDecoration
/// registers with TryAdd, so a leftover stub registration silently wins over the real one.
/// </summary>
[ScopedService(typeof(ITokenService))]
public sealed class StubTokenService : ITokenService
{
    public Task<TokenPair?> IssueAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<TokenPair?>(null);

    public Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<TokenPair?>(null);

    public Task<bool> RevokeAsync(string rawToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
