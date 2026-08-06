using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.Resilience;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.LLMs.Base;

/// <summary>
/// Simplified base class for HTTP-based LLM providers.
/// Contains only HTTP adapter concerns - no business logic.
/// Business logic (caching, retry, configuration logic) has been moved to domain services.
/// </summary>
public abstract partial class HttpLlmProviderBase : ILlmProvider, IStreamingLlmProvider, IDisposable
{
    /// <summary>The LLM configuration.</summary>
    protected LlmConfig Config { get; }

    /// <inheritdoc />
    public LlmConfig? BaseConfig => Config;

    /// <summary>The HTTP client factory for creating named HTTP clients.</summary>
    protected IHttpClientFactory HttpClientFactory { get; }

    /// <summary>
    /// The logger for this provider. Kept as a <see langword="protected"/> field (rather than a
    /// property) because the <c>[LoggerMessage]</c> source generator used by derived providers'
    /// partial logging classes resolves the inherited <see cref="ILogger"/> instance via a field;
    /// exposing it as a property breaks generation (SYSLIB1019).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Must remain a protected field: the [LoggerMessage] source generator in derived partial classes resolves the inherited ILogger via a field; a property breaks generation (SYSLIB1019).")]
    protected readonly ILogger Logger;

    /// <summary>Shared JSON serializer options with camelCase and unsafe relaxed escaping.</summary>
    protected JsonSerializerOptions JsonOptions { get; }

    /// <summary>The Polly resilience policy wrapping all outbound HTTP requests.</summary>
    protected IAsyncPolicy<HttpResponseMessage> ResiliencePolicy { get; }
    private bool _disposed;

    /// <summary>
    /// Optional host-registered observer of retry activity (set post-construction by the host's
    /// DI wiring), so a UI can show "reconnecting…" during backoff waits instead of a silent
    /// stall. Null (the default) keeps behaviour unchanged: retries are only logged.
    /// </summary>
    public ILlmRetryObserver? RetryObserver { get; set; }

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// What this provider's API supports. Overridden by each concrete provider; a provider
    /// that declares nothing is assumed to support nothing, so no cross-cutting option is
    /// written to the wire on its behalf.
    /// </summary>
    public virtual LlmProviderCapabilities Capabilities => LlmProviderCapabilities.Unknown;

    /// <summary>Initializes a new instance of <see cref="HttpLlmProviderBase"/>.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">Optional logger.</param>
    protected HttpLlmProviderBase(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger? logger = null)
        : this(config, httpClientFactory, resiliencePolicy: null, logger)
    {
    }

    /// <summary>Initializes a new instance of <see cref="HttpLlmProviderBase"/> with an explicit resilience policy.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="resiliencePolicy">Optional custom resilience policy; defaults to the standard LLM API policy.</param>
    /// <param name="logger">Optional logger.</param>
    protected HttpLlmProviderBase(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        HttpClientFactory = httpClientFactory;
        Logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        // The retry budget comes from Llm:MaxRetries (default 10); the hook reads RetryObserver
        // at fire time, so a host wiring the observer after construction is still seen.
        // Clamped: Polly rejects a negative retry count at construction.
        ResiliencePolicy = resiliencePolicy ?? ResiliencePolicies.GetLlmApiPolicy(
            Logger, Math.Max(0, config.MaxRetries),
            onRetry: (attempt, delay, reason) => NotifyRetryScheduled(attempt, delay, reason));

        JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = SerializationDefaults.JsonMaxDepth
        };
    }

    /// <summary>
    /// Generates a response from the LLM.
    /// Pure HTTP adapter - no business logic.
    /// </summary>
    public abstract Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chat interface - delegates to GenerateAsync.
    /// </summary>
    public virtual async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        // Handle null or empty messages by sending empty prompt
        if (messages == null || messages.Length == 0)
        {
            return await GenerateAsync(string.Empty, config, cancellationToken).ConfigureAwait(false);
        }

        // Simple conversion of messages to prompt - pure adapter logic
        var prompt = ConvertMessagesToPrompt(messages);
        return await GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an HttpClient configured for this provider.
    /// Pure HTTP configuration - no business logic.
    /// </summary>
    protected virtual HttpClient CreateHttpClient(LlmConfig? requestConfig = null)
    {
        var client = HttpClientFactory.CreateClient(GetType().Name);
        var effectiveConfig = requestConfig ?? Config;

        try
        {
            // Simple authorization header setup
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(effectiveConfig.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", effectiveConfig.ApiKey);
            }
#pragma warning restore CS0618

            // Apply timeouts from configuration
            client.Timeout = TimeSpan.FromSeconds(effectiveConfig.TimeoutSeconds);

            // Allow derived classes to add headers
            ConfigureHttpClient(client, effectiveConfig);
        }
        catch (InvalidOperationException)
        {
            // Client has already started a request (e.g., during retry with a reused client).
            // Properties cannot be modified after the first request; continue with existing settings.
        }

        return client;
    }

    /// <summary>
    /// Allows derived classes to configure the HttpClient.
    /// Pure HTTP configuration hook.
    /// </summary>
    protected virtual void ConfigureHttpClient(HttpClient client, LlmConfig config)
    {
        // Default implementation does nothing.
        // Derived classes can override to add provider-specific headers.
    }

    /// <summary>
    /// Executes an HTTP request.
    /// Pure HTTP execution - no retry logic or business policies.
    /// </summary>
    protected Task<HttpResponseMessage> ExecuteHttpRequestAsync(
        HttpRequestMessage request,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteHttpRequestCoreAsync();

        async Task<HttpResponseMessage> ExecuteHttpRequestCoreAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Capture the original request content so we can recreate it on retries
            // (HttpContent is disposed after SendAsync)
            byte[]? contentBytes = null;
            string? contentType = null;
            if (request.Content != null)
            {
                contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                contentType = request.Content.Headers.ContentType?.ToString();
            }

            try
            {
                return await ResiliencePolicy.ExecuteAsync(async (ct) =>
                {
                    // The clone is owned by this attempt: its body is buffered below, so the
                    // request (and its content) can be disposed once SendAsync returns — the
                    // response keeps an independent, buffered copy (ANT-006/R10.2).
                    using var retryRequest = CloneHttpRequest(request, contentBytes, contentType);
                    var httpClient = CreateHttpClient(config);
                    var response = await httpClient.SendAsync(retryRequest, ct).ConfigureAwait(false);
                    // Read content immediately to avoid disposal issues
                    if (response.Content != null)
                    {
                        await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
                    }
                    return response;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (BrokenCircuitException ex)
            {
                LogCircuitBreakerOpen(ex);
                throw new HttpRequestException("LLM API is temporarily unavailable due to repeated failures. Please try again later.", ex);
            }
            catch (TimeoutRejectedException ex)
            {
                LogRequestTimedOut(ex);
                throw new HttpRequestException("LLM API request timed out. Please try again later.", ex);
            }
            finally
            {
                NotifyCallSettled();
            }
        }
    }

    /// <summary>Best-effort retry notification — never allowed to fail the call.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Observer fault barrier: a faulty host observer must degrade to unobserved retries, never to a failed LLM call.")]
    private void NotifyRetryScheduled(int attempt, TimeSpan delay, string reason, string? host = null)
    {
        var observer = RetryObserver;
        if (observer is null) return;
        try
        {
            observer.OnRetryScheduled(new LlmRetryEvent
            {
                Provider = Name,
                Host = host ?? Config.BaseUrl?.Host ?? string.Empty,
                Attempt = attempt,
                MaxRetries = Config.MaxRetries,
                Delay = delay,
                Reason = reason,
            });
        }
        catch { /* observer fault barrier */ }
    }

    /// <summary>Best-effort settle notification — never allowed to fail the call.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Observer fault barrier: a faulty host observer must degrade to unobserved retries, never to a failed LLM call.")]
    private void NotifyCallSettled()
    {
        try { RetryObserver?.OnCallSettled(); }
        catch { /* observer fault barrier */ }
    }

    /// <summary>
    /// Creates a clone of an HttpRequestMessage for retry scenarios.
    /// HttpRequestMessage cannot be reused after being sent, so a new instance is created
    /// with the same method, URI, headers, and content.
    /// </summary>
    private static HttpRequestMessage CloneHttpRequest(
        HttpRequestMessage original,
        byte[]? contentBytes,
        string? contentType)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (contentBytes != null)
        {
            clone.Content = new ByteArrayContent(contentBytes);
            if (contentType != null)
            {
                clone.Content.Headers.Remove("Content-Type");
                clone.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }
        }

        return clone;
    }

    /// <summary>
    /// Converts LlmMessage array to a single prompt string.
    /// Pure data transformation - no business logic.
    /// </summary>
    protected virtual string ConvertMessagesToPrompt(LlmMessage[] messages)
    {
        if (messages == null || messages.Length == 0)
            return string.Empty;

        return string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
    }

    /// <summary>
    /// Creates an error response from HTTP response.
    /// Pure data transformation - no business logic.
    /// </summary>
    protected virtual Task<LlmResponse> CreateErrorResponseAsync(
        HttpResponseMessage httpResponse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpResponse);
        return CreateErrorResponseCoreAsync();

        async Task<LlmResponse> CreateErrorResponseCoreAsync()
        {
            var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            LogHttpError(httpResponse.StatusCode, errorContent);

            return new LlmResponse
            {
                Content = string.Empty,
                TokensUsed = 0,
                Model = Config.Model,
                Metadata = new Dictionary<string, object>
                {
                    ["http_status_code"] = (int)httpResponse.StatusCode,
                    ["error_content"] = errorContent,
                    ["provider"] = Name
                }
            };
        }
    }

    /// <summary>
    /// Serialize object to JSON.
    /// Pure utility method.
    /// </summary>
    protected string SerializeToJson(object obj)
    {
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    /// <summary>
    /// Deserialize JSON to object.
    /// Pure utility method.
    /// </summary>
    protected T? DeserializeFromJson<T>(string json)
    {
        if (string.IsNullOrEmpty(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogJsonDeserializationFailed(ex, json);
            return default;
        }
    }

    /// <summary>
    /// Whether this provider supports streaming. True by default for HTTP-based providers.
    /// </summary>
    public virtual bool SupportsStreaming => true;

    /// <summary>
    /// Generates a streaming response. Override in derived classes for provider-specific SSE parsing.
    /// Default implementation falls back to non-streaming.
    /// </summary>
    public virtual async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Default fallback: non-streaming response as single chunk
        var response = await GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Content))
        {
            yield return response.Content;
        }
    }

    /// <summary>
    /// Streams a multi-message chat completion. Default implementation is the
    /// "buffered-then-streaming" fallback: one <see cref="LlmStreamEventKind.ContentDelta"/>
    /// carrying the full non-streaming <see cref="ILlmProvider.ChatAsync"/> content, then the
    /// <see cref="LlmStreamEventKind.Completed"/> event. Providers with a native SSE chat
    /// path override this (see <c>OpenAICompatibleProviderBase</c>).
    /// </summary>
    public virtual async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Content))
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    /// <summary>
    /// Builds the exception that a rejected streaming request must fail with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GenerateStreamingAsync"/> returns <c>IAsyncEnumerable&lt;string&gt;</c>: there is
    /// no metadata dictionary to put an error in, the way <see cref="ILlmProvider.GenerateAsync"/>
    /// has. Every native override used to log the status code and <c>yield break</c>, so a caller
    /// received an empty sequence that ended normally — indistinguishable from a model with
    /// nothing to say. The Kimi campaign of 2026-08-03 measured the cost: M3 reported
    /// <c>0 chunk(s), 0 char(s)</c> while the API had answered
    /// <c>invalid temperature: only 1 is allowed for this model</c>, and that sentence existed
    /// nowhere in the archived evidence.
    /// </para>
    /// <para>
    /// Throwing rather than returning is not a new obligation on callers: a malformed SSE chunk
    /// already reaches them as a <c>JsonException</c> from the parse inside the read loop. This
    /// only means a request the vendor refused now fails as loudly as one it mangled.
    /// <see cref="HttpRequestException"/> carries the status in
    /// <see cref="HttpRequestException.StatusCode"/>, so no new exception type is needed.
    /// </para>
    /// </remarks>
    /// <param name="response">The non-success response, still holding its body.</param>
    /// <param name="providerDisplayName">Provider name, as the reader sees it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exception to throw, carrying the vendor's own words.</returns>
    protected static async Task<HttpRequestException> StreamingRejectionAsync(
        HttpResponseMessage response,
        string providerDisplayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Same phrasing as the buffered path's error metadata, so a reader comparing M1 and M3
        // sees one wording and not two.
        return new HttpRequestException(
            $"{providerDisplayName} API error: {response.StatusCode} - {Security.LogSanitizer.SanitizeString(body)}",
            inner: null,
            statusCode: response.StatusCode);
    }

    /// <summary>
    /// Reads an SSE (Server-Sent Events) stream and yields data payloads.
    /// Handles "data: {json}" format with "data: [DONE]" terminator.
    /// </summary>
    protected static async IAsyncEnumerable<string> ReadSseStreamAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in ReadStreamLinesAsync(response, cancellationToken).ConfigureAwait(false))
        {
            if (!line.StartsWith(HttpDefaults.SseDataPrefix, StringComparison.Ordinal))
                continue;

            var data = line[HttpDefaults.SseDataPrefix.Length..];

            if (data == HttpDefaults.SseDoneMarker)
                yield break;

            yield return data;
        }
    }

    /// <summary>
    /// Reads an NDJSON stream (newline-delimited JSON) and yields each JSON line.
    /// Used by Ollama.
    /// </summary>
    protected static async IAsyncEnumerable<string> ReadNdjsonStreamAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in ReadStreamLinesAsync(response, cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    /// <summary>
    /// Reads non-empty lines from an HTTP response stream.
    /// Shared implementation used by both SSE and NDJSON stream parsers.
    /// </summary>
    protected static IAsyncEnumerable<string> ReadStreamLinesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        return ReadStreamLinesCoreAsync(cancellationToken);

        async IAsyncEnumerable<string> ReadStreamLinesCoreAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(line))
                    continue;

                yield return line;
            }
        }
    }

    // Streaming connect retry: quick first retries (0.5 s doubling), every wait capped at
    // DefaultRetryMaxDelay so the Llm:MaxRetries budget (default 10) degrades to a bounded
    // ~30 s cadence. The budget is shared with the buffered path's policy; the visibility
    // that makes a long budget acceptable on an interactive turn comes from RetryObserver.
    private static readonly TimeSpan StreamingRetryBaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StreamingRetryAfterCap = Orkeon.Domain.Constants.Resilience.ResilienceDefaults.DefaultRetryMaxDelay;

    /// <summary>
    /// Sends a streaming HTTP request (without buffering the response).
    /// </summary>
    /// <remarks>
    /// The buffered path runs under <see cref="ResiliencePolicy"/>; this one cannot (the
    /// response body escapes to the caller), so it retries the CONNECT/headers phase itself:
    /// a transient transport failure (socket/DNS, e.g. "Resource temporarily unavailable"),
    /// a client-side connect timeout, or a retriable status (408/429/5xx) before any of the
    /// body was consumed. Once headers are returned to the caller, a mid-stream failure is
    /// never retried here — replaying a partially-consumed stream is the caller's decision.
    /// </remarks>
    protected Task<HttpResponseMessage> SendStreamingRequestAsync(
        HttpClient client,
        Uri endpoint,
        string jsonPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        return SendStreamingRequestCoreAsync();

        async Task<HttpResponseMessage> SendStreamingRequestCoreAsync()
        {
            var maxAttempts = Math.Max(1, Config.MaxRetries + 1);
            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        // The response escapes (the caller streams its body via ResponseHeadersRead), but the
                        // request body is fully transmitted once SendAsync returns, so the request — and the
                        // content it owns — can be disposed here without touching the live response stream
                        // (ANT-006/R10.2: dispose by real lifetime, not lexically). One request per attempt:
                        // HttpRequestMessage cannot be resent.
                        using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, HttpDefaults.JsonContentType);
                        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };

                        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                        if (attempt >= maxAttempts || !IsRetriableStreamingStatus(response.StatusCode))
                            return response;

                        var reason = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var delay = RetryAfterDelay(response) ?? StreamingBackoff(attempt);
                        LogStreamingConnectRetry(attempt, delay.TotalMilliseconds, reason);
                        NotifyRetryScheduled(attempt, delay, reason, endpoint?.Host);
                        response.Dispose();
                        await System.Threading.Tasks.Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (HttpRequestException ex) when (attempt < maxAttempts)
                    {
                        LogStreamingConnectRetry(attempt, StreamingBackoff(attempt).TotalMilliseconds, ex.Message);
                        NotifyRetryScheduled(attempt, StreamingBackoff(attempt), ex.Message, endpoint?.Host);
                        await System.Threading.Tasks.Task.Delay(StreamingBackoff(attempt), cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
                    {
                        // HttpClient.Timeout expired before headers — not a user cancellation.
                        LogStreamingConnectRetry(attempt, StreamingBackoff(attempt).TotalMilliseconds, ex.Message);
                        NotifyRetryScheduled(attempt, StreamingBackoff(attempt), ex.Message, endpoint?.Host);
                        await System.Threading.Tasks.Task.Delay(StreamingBackoff(attempt), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                NotifyCallSettled();
            }
        }
    }

    /// <summary>Same retriable statuses as the buffered path's policies (408, 429, 5xx).</summary>
    private static bool IsRetriableStreamingStatus(System.Net.HttpStatusCode status)
        => (int)status >= 500
           || status == System.Net.HttpStatusCode.RequestTimeout
           || status == System.Net.HttpStatusCode.TooManyRequests;

    private static TimeSpan StreamingBackoff(int attempt)
    {
        // Cap in double space BEFORE converting: 2^(n−1) ms overflows TimeSpan for a large
        // configured budget.
        var ms = StreamingRetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        return ms >= StreamingRetryAfterCap.TotalMilliseconds
            ? StreamingRetryAfterCap
            : TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>Server-provided Retry-After delta when present, capped so an interactive turn never parks for minutes.</summary>
    private static TimeSpan? RetryAfterDelay(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is null) return null;
        return delta.Value <= StreamingRetryAfterCap ? delta.Value : StreamingRetryAfterCap;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "LLM streaming connect retry {Attempt} in {Delay}ms. Reason: {Reason}")]
    private partial void LogStreamingConnectRetry(int attempt, double delay, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "LLM API circuit breaker is open. Requests are being rejected.")]
    private partial void LogCircuitBreakerOpen(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "LLM API request timed out after resilience policy timeout.")]
    private partial void LogRequestTimedOut(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "LLM HTTP error: {StatusCode} - {Error}")]
    private partial void LogHttpError(System.Net.HttpStatusCode statusCode, string error);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Failed to deserialize JSON: {Json}")]
    private partial void LogJsonDeserializationFailed(Exception ex, string json);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources when disposing.</summary>
    /// <param name="disposing">True if called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // HttpClient instances are managed by IHttpClientFactory
                // No explicit disposal needed
            }
            _disposed = true;
        }
    }
}
