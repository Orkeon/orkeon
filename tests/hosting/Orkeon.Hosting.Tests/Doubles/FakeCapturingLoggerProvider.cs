using Microsoft.Extensions.Logging;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>Hand-written <see cref="ILoggerProvider"/> recording every entry as (level, formatted message).</summary>
public sealed class FakeCapturingLoggerProvider : ILoggerProvider
{
    /// <summary>Entries logged so far.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(FakeCapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Entries.Add((logLevel, formatter(state, exception)));
    }
}
