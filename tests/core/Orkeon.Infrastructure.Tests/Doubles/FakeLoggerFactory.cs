using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Records every log entry of every logger it creates, for tests that assert a warning.</summary>
internal sealed class FakeLoggerFactory : ILoggerFactory
{
    private readonly object _gate = new();
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];

    /// <summary>A snapshot of the entries logged so far.</summary>
    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
                return [.. _entries];
        }
    }

    public ILogger CreateLogger(string categoryName) => new FakeLogger(this, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class FakeLogger(FakeLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._gate)
                owner._entries.Add((category, logLevel, formatter(state, exception)));
        }
    }
}
