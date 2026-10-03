using Microsoft.Extensions.Logging;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>
/// An <see cref="ILogger"/> recording every entry with its level and formatted message, so a test
/// can assert on what the REPL said at startup without a console.
/// </summary>
public sealed class CapturingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly Lock _gate = new();

    /// <summary>Snapshot of the entries recorded so far.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get { lock (_gate) { return [.. _entries]; } }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_gate)
            _entries.Add((logLevel, formatter(state, exception)));
    }
}
