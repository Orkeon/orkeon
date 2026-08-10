using Microsoft.Extensions.Logging;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ILoggerProvider"/> capturing every log entry in memory so tests
/// can assert on emitted warnings without a console. Thread-safe: hosts log from arbitrary
/// threads during startup.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<CapturedLogEntry> _entries = [];
    private readonly Lock _lock = new();

    /// <summary>Snapshot of the entries captured so far.</summary>
    public IReadOnlyList<CapturedLogEntry> Entries
    {
        get { lock (_lock) { return [.. _entries]; } }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
        // Nothing to release; entries stay readable after the host is disposed.
    }

    private void Add(CapturedLogEntry entry)
    {
        lock (_lock) { _entries.Add(entry); }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            owner.Add(new CapturedLogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>One captured log entry.</summary>
public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);
