namespace Orkeon.Application.Interfaces.Logging;

/// <summary>
/// Logs raw HTTP exchanges between Orkeon and LLM providers.
/// Captures request and response headers + payloads for observability and debugging.
/// </summary>
public interface ILlmExchangeLogger
{
    /// <summary>
    /// Logs a complete LLM HTTP exchange (request + response).
    /// </summary>
    /// <param name="exchange">The captured exchange record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous logging operation.</returns>
    System.Threading.Tasks.Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a complete HTTP exchange with an LLM provider.
/// Headers are sanitized (API keys redacted) before storage.
/// </summary>
public sealed record LlmExchangeRecord
{
    /// <summary>Unique identifier for this exchange.</summary>
    public required string ExchangeId { get; init; }

    /// <summary>UTC timestamp when the request was sent.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>LLM provider name (e.g., "openai", "anthropic", "ollama").</summary>
    public required string Provider { get; init; }

    /// <summary>HTTP method (POST, GET, etc.).</summary>
    public required string HttpMethod { get; init; }

    /// <summary>Full request URL (endpoint).</summary>
    public required Uri? RequestUrl { get; init; }

    /// <summary>Request headers (sanitized — API keys redacted).</summary>
    public required IReadOnlyDictionary<string, string[]> RequestHeaders { get; init; }

    /// <summary>Request body (JSON payload sent to the LLM).</summary>
    public required string RequestBody { get; init; }

    /// <summary>HTTP status code of the response.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Response headers.</summary>
    public required IReadOnlyDictionary<string, string[]> ResponseHeaders { get; init; }

    /// <summary>Response body (JSON payload received from the LLM).</summary>
    public required string ResponseBody { get; init; }

    /// <summary>Round-trip duration of the HTTP call.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Whether the exchange was successful (2xx status).</summary>
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

    /// <summary>Optional error message if the call failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Model name extracted from the request payload, if available.</summary>
    public string? Model { get; init; }

    /// <summary>Whether this was a streaming request.</summary>
    public bool IsStreaming { get; init; }
}
