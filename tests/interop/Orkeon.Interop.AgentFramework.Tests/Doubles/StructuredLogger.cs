using Microsoft.Extensions.Logging;

namespace Orkeon.Interop.AgentFramework.Tests.Doubles;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps every entry with its event id and its structured fields —
/// the named values of the message template — so a test can tell a structured warning from a sentence.
/// </summary>
public sealed class StructuredLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    /// <summary>One entry: its level, event id, rendered message and named fields.</summary>
    public sealed record Entry(LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> Fields);

    /// <summary>The entries logged so far, oldest first.</summary>
    public IReadOnlyList<Entry> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<string, object?>();
        lock (_gate)
            _entries.Add(new Entry(logLevel, eventId, formatter(state, exception), fields));
    }
}
