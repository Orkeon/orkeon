using Orkeon.Application.Interfaces.Logging;

namespace Orkeon.Infrastructure.Logging;

/// <summary>
/// Dispatches each exchange record to multiple <see cref="ILlmExchangeLogger"/> implementations.
/// Use this to combine file logging (full capture) with structured logging (monitoring) simultaneously.
/// </summary>
public sealed class CompositeLlmExchangeLogger : ILlmExchangeLogger
{
    private readonly IReadOnlyList<ILlmExchangeLogger> _loggers;

    /// <summary>
    /// Initializes a new instance of <see cref="CompositeLlmExchangeLogger"/>.
    /// </summary>
    /// <param name="loggers">The logger implementations to dispatch to.</param>
    public CompositeLlmExchangeLogger(IEnumerable<ILlmExchangeLogger> loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        _loggers = loggers.ToArray();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="CompositeLlmExchangeLogger"/>.
    /// </summary>
    /// <param name="loggers">The logger implementations to dispatch to.</param>
    public CompositeLlmExchangeLogger(params ILlmExchangeLogger[] loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        _loggers = loggers;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fan-out barrier: each logger's failure is collected so one faulty logger cannot prevent the others from running; collected failures are rethrown as an AggregateException.")]
    public async Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
    {
        // Fan out to all loggers — collect exceptions without stopping
        List<Exception>? exceptions = null;

        foreach (var logger in _loggers)
        {
            try
            {
                await logger.LogExchangeAsync(exchange, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions ??= [];
                exceptions.Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more LLM exchange loggers failed", exceptions);
        }
    }
}
