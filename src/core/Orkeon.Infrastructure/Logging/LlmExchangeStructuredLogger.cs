using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Logging;

namespace Orkeon.Infrastructure.Logging;

/// <summary>
/// Logs LLM exchange summaries through the standard <see cref="ILogger"/> pipeline.
/// Emits structured log events at <see cref="LogLevel.Information"/> for successful exchanges
/// and <see cref="LogLevel.Warning"/> for failures.
/// <para>
/// Does NOT log full payloads — use <see cref="LlmExchangeJsonLogger"/> for full capture.
/// This logger is intended for real-time monitoring via console/OpenTelemetry/Seq/etc.
/// </para>
/// </summary>
public sealed partial class LlmExchangeStructuredLogger : ILlmExchangeLogger
{
    private readonly ILogger<LlmExchangeStructuredLogger> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="LlmExchangeStructuredLogger"/>.
    /// </summary>
    /// <param name="logger">The structured logger.</param>
    public LlmExchangeStructuredLogger(ILogger<LlmExchangeStructuredLogger> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exchange);

        var entry = new LlmExchangeLogEntry(
            exchange.ExchangeId,
            exchange.Provider,
            exchange.Model ?? "unknown",
            exchange.HttpMethod,
            exchange.RequestUrl?.ToString() ?? "unknown",
            exchange.StatusCode,
            exchange.Duration.TotalMilliseconds,
            exchange.IsStreaming,
            exchange.ErrorMessage ?? "Unknown error");

        if (exchange.IsSuccess)
            LogSuccessfulExchange(entry);
        else
            LogFailedExchange(entry);

        return Task.CompletedTask;
    }

    private void LogSuccessfulExchange(LlmExchangeLogEntry entry) =>
        LogExchangeSucceeded(
            entry.ExchangeId, entry.Provider, entry.Model,
            entry.Method, entry.Url, entry.StatusCode,
            entry.DurationMs, entry.IsStreaming);

    private void LogFailedExchange(LlmExchangeLogEntry entry) =>
        LogExchangeFailed(
            entry.ExchangeId, entry.Provider, entry.Model,
            entry.Method, entry.Url, entry.StatusCode,
            entry.DurationMs, entry.Error);

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "LLM exchange {ExchangeId}: {Provider}/{Model} {Method} {Url} \u2192 {StatusCode} in {DurationMs:F1}ms (streaming={IsStreaming})")]
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
        Justification = "[LoggerMessage] binds one method parameter per {Placeholder} of the " +
        "message template; a grouping record would collapse the eight structured fields into " +
        "a single ToString-ed property and lose them for Seq/OpenTelemetry queries. Passing them " +
        "through ILogger.Log instead is barred by CA1848, which this repository treats as an error.")]
    private partial void LogExchangeSucceeded(
        string exchangeId, string provider, string model,
        string method, string url, int statusCode,
        double durationMs, bool isStreaming);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "LLM exchange {ExchangeId} FAILED: {Provider}/{Model} {Method} {Url} \u2192 {StatusCode} in {DurationMs:F1}ms \u2014 {Error}")]
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
        Justification = "[LoggerMessage] binds one method parameter per {Placeholder} of the " +
        "message template; a grouping record would collapse the eight structured fields into " +
        "a single ToString-ed property and lose them for Seq/OpenTelemetry queries. Passing them " +
        "through ILogger.Log instead is barred by CA1848, which this repository treats as an error.")]
    private partial void LogExchangeFailed(
        string exchangeId, string provider, string model,
        string method, string url, int statusCode,
        double durationMs, string error);
}

/// <summary>
/// Flat DTO for structured log fields emitted per LLM exchange.
/// </summary>
internal sealed record LlmExchangeLogEntry(
    string ExchangeId,
    string Provider,
    string Model,
    string Method,
    string Url,
    int StatusCode,
    double DurationMs,
    bool IsStreaming,
    string Error);
