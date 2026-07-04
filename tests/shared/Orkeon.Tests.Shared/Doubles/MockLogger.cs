using Microsoft.Extensions.Logging;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Manual mock implementation of ILogger&lt;T&gt; that tracks log calls.
/// </summary>
public sealed class MockLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _logEntries = [];

    /// <summary>
    /// Gets all recorded log entries as (LogLevel, Message) tuples.
    /// </summary>
    public IReadOnlyList<(LogLevel Level, string Message)> LogEntries => _logEntries.AsReadOnly();

    /// <summary>
    /// Gets the total number of log calls made.
    /// </summary>
    public int LogCallCount => _logEntries.Count;

    /// <summary>
    /// Gets the last log level used, or null if no logs recorded.
    /// </summary>
    public LogLevel? LastLogLevel => _logEntries.Count > 0 ? _logEntries[^1].Level : null;

    /// <summary>
    /// Gets the last log message, or null if no logs recorded.
    /// </summary>
    public string? LastLogMessage => _logEntries.Count > 0 ? _logEntries[^1].Message : null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _logEntries.Add((logLevel, message));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    /// <summary>
    /// Clears all recorded log entries.
    /// </summary>
    public void Clear() => _logEntries.Clear();

    /// <summary>
    /// Returns log entries filtered by the specified log level.
    /// </summary>
    public IReadOnlyList<(LogLevel Level, string Message)> GetEntriesByLevel(LogLevel level)
        => _logEntries.Where(e => e.Level == level).ToList().AsReadOnly();

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { /* Intentionally empty — NullScope has no resources to release */ }
    }
}
