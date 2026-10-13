using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// <see cref="ILoggerFactory"/> that hands out a <see cref="RecordingCategoryLogger"/> per category,
/// all sharing one <see cref="Lines"/> queue.
/// </summary>
internal sealed class RecordingCategoryLoggerFactory(ConcurrentQueue<CategoryLogLine> lines) : ILoggerFactory
{
    /// <summary>Every line recorded across the loggers created.</summary>
    public ConcurrentQueue<CategoryLogLine> Lines { get; } = lines;

    public ILogger CreateLogger(string categoryName) => new RecordingCategoryLogger(categoryName, Lines);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
