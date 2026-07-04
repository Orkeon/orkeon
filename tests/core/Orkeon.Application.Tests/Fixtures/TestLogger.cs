using Microsoft.Extensions.Logging;
namespace Orkeon.Application.Tests.Fixtures;

/// <summary>
/// Test double for ILogger used in unit tests.
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _logEntries = [];

    public IReadOnlyList<LogEntry> LogEntries => _logEntries.AsReadOnly();

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logEntries.Add(new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            Timestamp = DateTime.UtcNow
        });
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return new NoOpDisposable();
    }

    public bool HasLoggedError()
    {
        return _logEntries.Any(e => e.LogLevel == LogLevel.Error);
    }

    public bool HasLoggedWarning()
    {
        return _logEntries.Any(e => e.LogLevel == LogLevel.Warning);
    }

    public bool HasLoggedMessage(string partialMessage)
    {
        return _logEntries.Any(e => e.Message?.Contains(partialMessage) ?? false);
    }

    public void Clear()
    {
        _logEntries.Clear();
    }

    public class LogEntry
    {
        public LogLevel LogLevel { get; set; }
        public EventId EventId { get; set; }
        public string? Message { get; set; }
        public Exception? Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
