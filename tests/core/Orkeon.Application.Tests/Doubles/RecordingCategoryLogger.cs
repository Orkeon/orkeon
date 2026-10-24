using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>One line recorded by <see cref="RecordingCategoryLogger"/>.</summary>
internal sealed record CategoryLogLine(string Category, LogLevel Level, string Message);

/// <summary>
/// <see cref="ILogger{TCategoryName}"/> of the orchestrator that records each line with the logger
/// category it was written under. Several instances share one <see cref="Lines"/> queue, so a test
/// sees which category received what.
/// </summary>
internal sealed class RecordingCategoryLogger(string category, ConcurrentQueue<CategoryLogLine> lines) : ILogger<ExecutionOrchestrator>
{
    /// <summary>Every line recorded, in order, across the loggers sharing the queue.</summary>
    public ConcurrentQueue<CategoryLogLine> Lines { get; } = lines;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>The lowest level this logger is enabled for; every level by default.</summary>
    public LogLevel MinimumLevel { get; init; } = LogLevel.Trace;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Enqueue(new CategoryLogLine(category, logLevel, formatter(state, exception)));
}
