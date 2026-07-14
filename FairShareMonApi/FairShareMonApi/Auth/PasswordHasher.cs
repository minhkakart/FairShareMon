using System.Collections.Concurrent;
using DiDecoration.Attributes;

namespace FairShareMonApi.Auth;

/// <summary>BCrypt password hashing. Work factor from <c>Auth:BcryptWorkFactor</c> (default 11).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);

    /// <summary>
    /// A throwaway hash produced with the SAME work factor as real hashes, to verify against when
    /// the account is unknown so login timing does not reveal whether the username exists. Computed
    /// once per work factor for the process lifetime.
    /// </summary>
    string CreateDummyHash();
}

[ScopedService(typeof(IPasswordHasher))]
public sealed class PasswordHasher(IConfiguration configuration) : IPasswordHasher
{
    public const int DefaultWorkFactor = 11;

    // Keyed by work factor so the dummy hash always tracks the configured cost (never a stale 11);
    // BCrypt hashing is expensive, so this computes at most once per distinct factor per process.
    private static readonly ConcurrentDictionary<int, string> DummyHashByWorkFactor = new();

    private readonly int _workFactor = configuration.GetValue("Auth:BcryptWorkFactor", DefaultWorkFactor);

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    public string CreateDummyHash() =>
        DummyHashByWorkFactor.GetOrAdd(_workFactor, factor => BCrypt.Net.BCrypt.HashPassword(Utils.Uuid.NewV7(), factor));
}
