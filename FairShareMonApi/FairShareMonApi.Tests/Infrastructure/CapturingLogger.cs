using Microsoft.Extensions.Logging;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// A minimal in-memory <see cref="ILogger{T}"/> for unit tests that need to assert a log was written
/// (e.g. the VietQR content provider's "falling back to the local builder" warning). Records the level
/// and rendered message of every entry.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    /// <summary>Every entry logged, in order (level + rendered message).</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public bool HasWarning => Entries.Any(entry => entry.Level == LogLevel.Warning);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
