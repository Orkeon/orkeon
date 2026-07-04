using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Captures every <see cref="ILogger.Log"/> invocation into a thread-safe list.
/// Replaces <c>Mock&lt;ILogger&gt;</c> usage in tests.
/// </summary>
public sealed class RecordingLogger : ILogger
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>Captured log entries in insertion order.</summary>
    public IReadOnlyCollection<LogEntry> Entries => _entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    /// <summary>True if at least one entry matches the predicate.</summary>
    public bool HasEntry(Func<LogEntry, bool> predicate) => Entries.Any(predicate);
}

/// <summary>Single record captured by <see cref="RecordingLogger"/>.</summary>
public sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
