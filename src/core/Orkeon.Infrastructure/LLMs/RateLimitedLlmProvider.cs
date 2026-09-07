using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Decorator that routes every <see cref="ILlmProvider"/> call through an
/// <see cref="ILlmRateLimiter"/> before it reaches the wrapped provider.
///
/// <para>
/// The YAML/agent path is already throttled inside <c>ExecutionOrchestrator</c>,
/// but the scripted path (<c>ctx.llm.complete</c> via <c>JsLlmFacade</c>) talks to
/// the provider directly — so a scripted dynamic fan-out (one spawned agent per
/// command, fired concurrently with <c>Promise.all</c>) would open N simultaneous
/// sockets with no throttling. Wrapping the provider handed to the scripting
/// engine makes that fan-out honour the <c>RateLimiting</c> appsettings block:
/// <c>MaxConcurrentRequests</c> bounds in-flight HTTP, the per-minute sliding
/// windows pace throughput, and <c>QueueLimit</c> holds the overflow instead of
/// drowning the provider.
/// </para>
/// <para>
/// Scope it to the scripted host only — do NOT register it as a global
/// <c>ILlmProvider</c> decorator, or the YAML path would be throttled twice
/// (once here, once in <c>ExecutionOrchestrator.AcquireLlmLeaseAsync</c>).
/// </para>
/// </summary>
public sealed partial class RateLimitedLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    /// <summary>Default number of times to retry a denied acquisition before giving up.</summary>
    public const int DefaultMaxAcquireRetries = 5;

    /// <summary>
    /// Number of distinct synthetic per-agent buckets used for the rate limiter's
    /// per-agent window. The scripted facade carries no agent identity, so calls are
    /// spread across a small fixed set of keys. This keeps two things in balance:
    /// (1) it stops every spawned command-agent from collapsing into one shared
    /// bucket — which would throttle the whole fan-out to <c>AgentRequestsPerMinute</c>;
    /// (2) it bounds how many per-agent sliding-window limiters the rate limiter
    /// allocates (a fresh key per call would create one limiter per request).
    /// </summary>
    public const int AgentBucketCount = 64;

    private readonly ILlmProvider _inner;
    private readonly ILlmRateLimiter _rateLimiter;
    private readonly ILogger<RateLimitedLlmProvider>? _logger;
    private readonly int _maxAcquireRetries;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);
    private int _callSeq;

    /// <summary>Initializes a new <see cref="RateLimitedLlmProvider"/>.</summary>
    /// <param name="inner">The provider to wrap.</param>
    /// <param name="rateLimiter">The rate limiter gating each call.</param>
    /// <param name="logger">Optional logger for denial/retry diagnostics.</param>
    /// <param name="maxAcquireRetries">Bounded retries on a denied acquisition.</param>
    public RateLimitedLlmProvider(
        ILlmProvider inner,
        ILlmRateLimiter rateLimiter,
        ILogger<RateLimitedLlmProvider>? logger = null,
        int maxAcquireRetries = DefaultMaxAcquireRetries)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _logger = logger;
        _maxAcquireRetries = maxAcquireRetries;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public LlmConfig? BaseConfig => _inner.BaseConfig;

    /// <inheritdoc />
    public async Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool SupportsStreaming => (_inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    /// <summary>
    /// Streaming passthrough (exp07 F5): the rate-limit lease is acquired before the first
    /// byte and held for the whole enumeration — the socket stays open for the stream's
    /// lifetime, so releasing earlier would let a fan-out exceed
    /// <c>MaxConcurrentRequests</c>. A non-streaming inner provider falls back to a
    /// buffered single-chunk stream (same throttling).
    /// </summary>
    /// <remarks>
    /// An unconfigured provider is exactly a non-streaming one — it declares
    /// <c>SupportsStreaming = false</c> — so the fallback is the path its refusal takes, and that
    /// refusal is an empty <c>Content</c> with the reason in the metadata. Yielding nothing would
    /// hand the caller a stream that ended normally on silence, which is the confusion the
    /// capability declaration was changed to remove; the refusal is thrown instead, in the same
    /// family as the providers' own streaming failures.
    /// </remarks>
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (_inner is IStreamingLlmProvider streaming && streaming.SupportsStreaming)
        {
            await foreach (var chunk in streaming.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
                yield return chunk;
            yield break;
        }

        var response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Content))
        {
            yield return response.Content;
            yield break;
        }

        if (BufferedFallbackRefusal(response) is { } refusal)
            throw refusal;
    }

    /// <summary>Metadata key every provider writes its refusal under (see <c>LlmResponseMetadata</c>).</summary>
    private const string ProviderErrorMetadataKey = "error";

    /// <summary>
    /// The exception the buffered fallback must fail with when the wrapped provider refused
    /// instead of answering. Returns <see langword="null"/> for a merely empty answer: a model
    /// with nothing to say still ends its stream normally.
    /// </summary>
    /// <param name="response">The buffered answer the fallback was going to yield.</param>
    private static HttpRequestException? BufferedFallbackRefusal(LlmResponse response)
        => response.Metadata.TryGetValue(ProviderErrorMetadataKey, out var value)
           && value?.ToString() is { Length: > 0 } error
            ? new HttpRequestException(
                $"{error}: the buffered fallback answered with no content, so the stream carries nothing.")
            : null;

    /// <inheritdoc cref="GenerateStreamingAsync" />
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (_inner is IStreamingLlmProvider streaming && streaming.SupportsStreaming)
        {
            await foreach (var ev in streaming.ChatStreamingAsync(messages, config, cancellationToken).ConfigureAwait(false))
                yield return ev;
            yield break;
        }

        var response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Content))
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    private async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        // Spread calls across a bounded set of synthetic agent keys (see AgentBucketCount).
        var bucket = (uint)Interlocked.Increment(ref _callSeq) % AgentBucketCount;
        var agentKey = $"scripted-{bucket}";

        var acquisition = await _rateLimiter.AcquireAsync(_inner.Name, agentKey, ct).ConfigureAwait(false);
        if (acquisition.IsAcquired)
            return acquisition.Lease!;

        // QueueLimit is sized to hold the whole fan-out, so a denial is unexpected.
        // Honour RetryAfter and retry a bounded number of times before failing the call.
        for (var attempt = 0; attempt < _maxAcquireRetries; attempt++)
        {
            var delay = acquisition.RetryAfter ?? DefaultRetryDelay;
            if (_logger is not null)
                LogRateLimitDenied(_logger, _inner.Name, acquisition.DenialReason, attempt + 1, _maxAcquireRetries, delay);
            await Task.Delay(delay, ct).ConfigureAwait(false);
            acquisition = await _rateLimiter.AcquireAsync(_inner.Name, agentKey, ct).ConfigureAwait(false);
            if (acquisition.IsAcquired)
                return acquisition.Lease!;
        }

        throw new InvalidOperationException(
            $"LLM rate limit exceeded for provider '{_inner.Name}' after {_maxAcquireRetries} retries: {acquisition.DenialReason}");
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "LLM rate limit denied for provider {Provider} ({Reason}); retry {Attempt}/{Max} after {Delay}.")]
    static partial void LogRateLimitDenied(ILogger logger, string provider, string? reason, int attempt, int max, TimeSpan delay);
}
