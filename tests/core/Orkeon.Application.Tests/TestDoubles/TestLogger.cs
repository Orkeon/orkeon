using Microsoft.Extensions.Logging;
namespace Orkeon.Application.Tests.TestDoubles;

public class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _logEntries = [];

    public IReadOnlyList<LogEntry> LogEntries => _logEntries;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return new NullScope();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logEntries.Add(new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception
        });
    }

    public bool HasLoggedError(string containsText)
    {
        return _logEntries.Any(e => e.LogLevel == LogLevel.Error && e.Message.Contains(containsText));
    }

    public bool HasLoggedInfo(string containsText)
    {
        return _logEntries.Any(e => e.LogLevel == LogLevel.Information && e.Message.Contains(containsText));
    }

    public class LogEntry
    {
        public LogLevel LogLevel { get; init; }
        public EventId EventId { get; init; }
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }
    }

    private class NullScope : IDisposable
    {
        public void Dispose() { }
    }
}
