using Microsoft.Extensions.Logging;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double: records every log entry (level + rendered message) so
/// tests can assert that rejections/exclusions are traced, never silent.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>All recorded entries, in emission order.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>True when any entry at <paramref name="level"/> contains <paramref name="fragment"/>.</summary>
    public bool Contains(LogLevel level, string fragment) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
