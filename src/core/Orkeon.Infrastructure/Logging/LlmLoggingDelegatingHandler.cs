using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Logging;

/// <summary>
/// HTTP DelegatingHandler that intercepts all LLM provider HTTP exchanges
/// and logs the full request/response (headers + payload) through <see cref="ILlmExchangeLogger"/>.
/// <para>
/// Registered via <see cref="IHttpClientFactory"/> named clients so that every
/// LLM provider automatically gets logging without code changes.
/// </para>
/// <para>
/// <b>Security</b>: All headers are sanitized through <see cref="LogSanitizer"/> before logging.
/// Sensitive headers (Authorization, api-key, x-api-key, x-goog-api-key, x-amz-*,
/// proxy-authorization, …) are redacted by name, and request/response bodies are passed
/// through pattern-based sanitization so API keys, Bearer tokens, and other secrets are
/// redacted automatically.
/// </para>
/// <para>
/// <b>Debug mode only</b>: LLM exchange capture records full request/response payloads
/// (prompts, tool results, headers). Even with sanitization, it should be treated as a
/// <i>debug</i> facility. Do not enable it in production without a data retention and log
/// rotation policy, as captured exchanges may still contain sensitive business content.
/// </para>
/// </summary>
public sealed partial class LlmLoggingDelegatingHandler : DelegatingHandler
{
    private readonly ILlmExchangeLogger _exchangeLogger;
    private readonly ILogger<LlmLoggingDelegatingHandler> _logger;
    private readonly LlmLoggingOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="LlmLoggingDelegatingHandler"/>.
    /// </summary>
    /// <param name="exchangeLogger">The exchange logger implementation.</param>
    /// <param name="logger">The structured logger.</param>
    /// <param name="options">Optional logging configuration.</param>
    public LlmLoggingDelegatingHandler(
        ILlmExchangeLogger exchangeLogger,
        ILogger<LlmLoggingDelegatingHandler> logger,
        LlmLoggingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(exchangeLogger);
        ArgumentNullException.ThrowIfNull(logger);
        _exchangeLogger = exchangeLogger;
        _logger = logger;
        _options = options ?? LlmLoggingOptions.Default;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Logging fault barrier in the finally block: a failure building or dispatching the exchange record is logged and swallowed so diagnostics logging never breaks the HTTP pipeline (the primary request exception is rethrown separately).")]
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendCoreAsync();

        async Task<HttpResponseMessage> SendCoreAsync()
        {
        var exchangeId = Guid.NewGuid().ToString("N")[..12];
        var timestamp = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // Capture request body before it gets disposed by SendAsync
        string requestBody = string.Empty;
        if (request.Content != null)
        {
            requestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        // Extract provider name from the named client (falls back to host)
        var provider = ExtractProviderName(request.RequestUri);

        HttpResponseMessage? response = null;
        string responseBody = string.Empty;
        int statusCode = 0;
        string? errorMessage = null;

        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            statusCode = (int)response.StatusCode;

            // Read the response body — buffer it so downstream consumers can still read it
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                errorMessage = $"HTTP {statusCode}: {TruncateForError(responseBody)}";
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            errorMessage = $"{ex.GetType().Name}: {LogSanitizer.SanitizeString(ex.Message)}";
            LogExchangeError(exchangeId, ex);
            throw;
        }
        finally
        {
            try
            {
                var exchange = BuildExchangeRecord(new RawExchangeCapture(
                    exchangeId, timestamp, provider, request,
                    requestBody, statusCode, response, responseBody,
                    stopwatch.Elapsed, errorMessage));

                // The option, honoured. LogStreamingExchanges was bound from configuration,
                // offered as a checkbox in both Studio surfaces and read by nothing: turning
                // it off still captured every streaming exchange. Streaming records are the
                // ones that carry a request and no usable response body, so an operator
                // silencing them is asking for something the writer can actually deliver.
                if (exchange.IsStreaming && !_options.LogStreamingExchanges)
                {
                    LogStreamingExchangeSkipped(exchangeId);
                }
                else
                {
                    // Fire-and-forget logging — never block the HTTP pipeline
                    _ = LogExchangeSafeAsync(exchange, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Never let logging failures propagate to the caller
                LogBuildRecordFailed(exchangeId, ex);
            }
        }
        }
    }

    private LlmExchangeRecord BuildExchangeRecord(RawExchangeCapture capture)
    {
        var sanitizedRequestHeaders = SanitizeHeaders(capture.Request.Headers, capture.Request.Content?.Headers);
        var sanitizedResponseHeaders = capture.Response != null
            ? SanitizeHeaders(capture.Response.Headers, capture.Response.Content?.Headers)
            : new Dictionary<string, string[]>();

        // Compact embedding arrays in response body before length-truncation,
        // so that the kept slice contains useful surrounding JSON instead of a
        // single 1.6 MB float array. Requests rarely contain embeddings, but
        // we apply the same transform for symmetry.
        var requestBodyForLog = _options.FullEmbeddingLog
            ? capture.RequestBody
            : CompactEmbeddings(capture.RequestBody);
        var responseBodyForLog = _options.FullEmbeddingLog
            ? capture.ResponseBody
            : CompactEmbeddings(capture.ResponseBody);

        // Truncate bodies if configured (bounds the CPU cost of the regex-based
        // body sanitization below for very large payloads).
        var truncatedRequestBody = TruncateBody(requestBodyForLog);
        var truncatedResponseBody = TruncateBody(responseBodyForLog);

        // Sanitize bodies: prompts and tool results may carry secrets (e.g. an API
        // key embedded in a JSON field). Pass them through LogSanitizer so known
        // secret patterns are redacted before logging.
        truncatedRequestBody = LogSanitizer.SanitizeString(truncatedRequestBody);
        truncatedResponseBody = LogSanitizer.SanitizeString(truncatedResponseBody);

        // Try to extract model name from request payload
        var model = ExtractModelFromPayload(capture.RequestBody);

        // Detect streaming from request body
        var isStreaming = DetectStreaming(capture.RequestBody);

        return new LlmExchangeRecord
        {
            ExchangeId = capture.ExchangeId,
            Timestamp = capture.Timestamp,
            Provider = capture.Provider,
            HttpMethod = capture.Request.Method.Method,
            RequestUrl = SanitizeUrl(capture.Request.RequestUri),
            RequestHeaders = sanitizedRequestHeaders,
            RequestBody = truncatedRequestBody,
            StatusCode = capture.StatusCode,
            ResponseHeaders = sanitizedResponseHeaders,
            ResponseBody = truncatedResponseBody,
            Duration = capture.Duration,
            ErrorMessage = capture.ErrorMessage,
            Model = model,
            IsStreaming = isStreaming
        };
    }

    /// <summary>
    /// Sanitizes all headers by redacting known secret patterns.
    /// Merges request/content headers into a single dictionary.
    /// </summary>
    private static Dictionary<string, string[]> SanitizeHeaders(
        System.Net.Http.Headers.HttpHeaders headers,
        System.Net.Http.Headers.HttpHeaders? contentHeaders)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            result[header.Key] = header.Value
                .Select(v => LogSanitizer.SanitizeHeaderValue(header.Key, v))
                .ToArray();
        }

        if (contentHeaders != null)
        {
            foreach (var header in contentHeaders)
            {
                result[header.Key] = header.Value
                    .Select(v => LogSanitizer.SanitizeHeaderValue(header.Key, v))
                    .ToArray();
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts the provider name from the request URI host.
    /// Maps known hosts to provider names.
    /// </summary>
    private static string ExtractProviderName(Uri? uri)
    {
        if (uri == null) return "unknown";

#pragma warning disable CA1308 // lowercase is the required wire/storage form, not a comparison normalization
        var host = uri.Host.ToLowerInvariant();
#pragma warning restore CA1308
        return host switch
        {
            _ when host.Contains("azure", StringComparison.Ordinal) => "azure-openai",
            _ when host.Contains("openai", StringComparison.Ordinal) => "openai",
            _ when host.Contains("anthropic", StringComparison.Ordinal) => "anthropic",
            _ when host.Contains("together", StringComparison.Ordinal) => "together",
            _ when host.Contains("deepseek", StringComparison.Ordinal) => "deepseek",
            _ when host.Contains("localhost", StringComparison.Ordinal) || host == "127.0.0.1" => "ollama",
            _ when host.Contains("huggingface", StringComparison.Ordinal) => "huggingface",
            _ => host
        };
    }

    /// <summary>
    /// Tries to extract the "model" field from a JSON request body.
    /// </summary>
    private static string? ExtractModelFromPayload(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("model", out var modelProp) &&
                modelProp.ValueKind == JsonValueKind.String)
            {
                return modelProp.GetString();
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — ignore
        }

        return null;
    }

    /// <summary>
    /// Detects whether the request is a streaming call (stream: true in JSON body).
    /// </summary>
    private static bool DetectStreaming(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("stream", out var streamProp) &&
                streamProp.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — ignore
        }

        return false;
    }

    /// <summary>
    /// Sanitizes a URL by redacting any query-string API keys.
    /// </summary>
    private static Uri? SanitizeUrl(Uri? url)
    {
        if (url is null)
            return null;

        // Sanitize the string form (redacts any secrets embedded in the URL) and re-parse.
        // If sanitization renders the value unparseable, drop it rather than log a raw URL.
        var sanitized = LogSanitizer.SanitizeString(url.ToString());
        return Uri.TryCreate(sanitized, UriKind.Absolute, out var result) ? result : null;
    }

    /// <summary>
    /// Matches a JSON property named "embedding" whose value is a flat array
    /// of numbers (no nested brackets). Used to compact embedding payloads in
    /// LLM exchange logs when <see cref="LlmLoggingOptions.FullEmbeddingLog"/>
    /// is false.
    /// </summary>
    [GeneratedRegex(
        @"""embedding""\s*:\s*\[(?<vals>[^\[\]]*)\]",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmbeddingArrayPattern();

    /// <summary>
    /// Replaces every <c>"embedding": [...]</c> in <paramref name="body"/> with
    /// a 4-element preview <c>[v0,v1,...,v_{n-2},v_{n-1}]</c> when the array
    /// has more than 4 elements. Output is intentionally not strict JSON
    /// (the literal <c>...</c> is invalid inside an array) — this is for
    /// human-readable log compaction only.
    /// </summary>
    internal static string CompactEmbeddings(string body)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("\"embedding\"", StringComparison.Ordinal))
            return body;

        return EmbeddingArrayPattern().Replace(body, match =>
        {
            var vals = match.Groups["vals"].Value;
            if (string.IsNullOrWhiteSpace(vals))
                return match.Value;

            var elements = vals.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (elements.Length <= 4)
                return match.Value;

            // Round-trip each kept element through Inv.Culture so the logged
            // representation is guaranteed culture-invariant (decimal point,
            // standard exponent notation), regardless of the upstream
            // serializer's quirks. Format "R" preserves IEEE 754 precision.
            var v0 = FormatInv(elements[0]);
            var v1 = FormatInv(elements[1]);
            var vN1 = FormatInv(elements[^2]);
            var vN  = FormatInv(elements[^1]);

            var compact = string.Concat(
                "\"embedding\":[",
                v0, ",", v1, ",...,", vN1, ",", vN,
                "]");
            return compact;
        });
    }

    /// <summary>
    /// Round-trips a numeric token through invariant culture using format "R"
    /// (lossless IEEE 754). Falls back to the raw token if parsing fails.
    /// </summary>
    private static string FormatInv(string raw)
    {
        return Orkeon.Domain.Common.Inv.TryParseDouble(raw, out var d)
            ? Orkeon.Domain.Common.Inv.ToString(d, "R")
            : raw;
    }

    /// <summary>
    /// Truncates a body string according to the configured max length.
    /// </summary>
    private string TruncateBody(string body)
    {
        if (_options.MaxBodyLengthChars <= 0 || string.IsNullOrEmpty(body))
            return body;

        if (body.Length <= _options.MaxBodyLengthChars)
            return body;

        return string.Concat(
            body.AsSpan(0, _options.MaxBodyLengthChars),
            $"... [TRUNCATED — {body.Length} chars total]");
    }

    /// <summary>
    /// Truncates response body for inclusion in error messages.
    /// </summary>
    private static string TruncateForError(string body)
    {
        const int maxErrorLen = 500;
        if (string.IsNullOrEmpty(body)) return "(empty)";
        return body.Length <= maxErrorLen ? body : body[..maxErrorLen] + "...";
    }

    /// <summary>
    /// Logs the exchange without propagating exceptions.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget logging barrier: a failure persisting the exchange record is logged and swallowed so it never faults the detached logging task.")]
    private async Task LogExchangeSafeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken)
    {
        try
        {
            await _exchangeLogger.LogExchangeAsync(exchange, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogPersistFailed(exchange.ExchangeId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM exchange {ExchangeId} failed during HTTP call")]
    private partial void LogExchangeError(string exchangeId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to build LLM exchange record {ExchangeId}")]
    private partial void LogBuildRecordFailed(string exchangeId, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped streaming LLM exchange {ExchangeId} (LogStreamingExchanges is off)")]
    private partial void LogStreamingExchangeSkipped(string exchangeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to persist LLM exchange {ExchangeId}")]
    private partial void LogPersistFailed(string exchangeId, Exception ex);
}

/// <summary>
/// Groups the raw HTTP exchange data captured before sanitization and truncation.
/// </summary>
internal sealed record RawExchangeCapture(
    string ExchangeId,
    DateTimeOffset Timestamp,
    string Provider,
    HttpRequestMessage Request,
    string RequestBody,
    int StatusCode,
    HttpResponseMessage? Response,
    string ResponseBody,
    TimeSpan Duration,
    string? ErrorMessage);

/// <summary>
/// Configuration options for LLM exchange logging.
/// </summary>
public sealed class LlmLoggingOptions
{
    /// <summary>Default options instance (no truncation).</summary>
    public static LlmLoggingOptions Default => new();

    /// <summary>
    /// Maximum number of characters to log for request/response bodies.
    /// Set to 0 or negative for no truncation. Default: 0 (no truncation).
    /// </summary>
    public int MaxBodyLengthChars { get; init; }

    /// <summary>
    /// Whether to log streaming exchanges. Default: true.
    /// Streaming responses are captured as the initial request only (response body will be empty).
    /// </summary>
    public bool LogStreamingExchanges { get; init; } = true;

    /// <summary>
    /// Whether to log embedding arrays at full length. Default: true.
    /// <para>
    /// When set to <c>false</c>, every JSON property named <c>"embedding"</c>
    /// whose value is a numeric array longer than 4 elements is replaced in
    /// the logged body with a 4-value preview of the form
    /// <c>"embedding":[v0,v1,...,v_{n-2},v_{n-1}]</c>. The full JSON envelope
    /// is preserved; only the array contents shrink. This brings a typical
    /// 1.6 MB embedding response (768-dim × N inputs) down to a few KB while
    /// keeping the request/response shape intact for diagnostics.
    /// </para>
    /// </summary>
    public bool FullEmbeddingLog { get; init; } = true;
}
