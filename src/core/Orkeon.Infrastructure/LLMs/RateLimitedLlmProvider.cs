using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// The host's <c>RateLimiting</c> at the entrance of a provider (GAP-38): each call to the provider
/// it wraps takes one lease of the host's <see cref="ILlmRateLimiter"/> — the global cap, the
/// provider's, the concurrency — for the time of the call, a stream for its whole enumeration.
/// </summary>
/// <remarks>
/// <para>
/// It is applied with the meter, around it, at the points that meter a provider
/// (<see cref="LlmProviderEntrance"/>): the factory, <c>AddOrkeonLlmProvider</c> and
/// <c>AddOrkeonLlmProfile</c>, the profile registry's <c>ForProvider</c>, the manager's resolver, the
/// planner of a C# crew. Every model call of the host takes a lease there, whoever makes it — an
/// agent's turn, the manager, the planner, the RAG pipeline, the judges, the cognitive memory, a
/// script's <c>ctx.llm</c> — and only one:
/// </para>
/// <list type="bullet">
/// <item><see cref="Wrap"/> leaves a provider already limited as it is, and one that runs its own
/// tools (<see cref="LlmProviderCapabilities.RunsOwnTools"/>, the Microsoft Agent Framework bridge):
/// a lease held through its tools would hold every call they make, and what it calls of Orkeon's
/// model is limited where that model is.</item>
/// <item>A call made inside another limited call takes no lease — a mark on the flow, like the
/// meter's: under <c>MaxConcurrentRequests: 1</c> the inner call would otherwise wait for the outer
/// one to end.</item>
/// </list>
/// <para>
/// A refused lease is retried after the limiter's <see cref="RateLimitAcquisition.RetryAfter"/>, five
/// times, then the call fails with the limiter's reason — the failed call an agent's turn reports.
/// </para>
/// </remarks>
public sealed class RateLimitedLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    /// <summary>How many times a refused lease is retried before the call fails.</summary>
    internal const int MaxAcquireRetries = 5;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Whether a limited call is in progress on the current flow.</summary>
    private static readonly AsyncLocal<bool> s_inLimitedCall = new();

    private readonly ILlmProvider _inner;
    private readonly ILlmRateLimiter _rateLimiter;
    private readonly int _maxAcquireRetries;

    internal RateLimitedLlmProvider(ILlmProvider inner, ILlmRateLimiter rateLimiter, int maxAcquireRetries = MaxAcquireRetries)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(rateLimiter);
        _inner = inner;
        _rateLimiter = rateLimiter;
        _maxAcquireRetries = maxAcquireRetries;
    }

    /// <summary>
    /// Limits <paramref name="provider"/> for <paramref name="rateLimiter"/>. Returns the provider itself
    /// when there is no limiter, when it is limited already, or when it runs its own tools.
    /// </summary>
    /// <param name="provider">The provider to limit.</param>
    /// <param name="rateLimiter">The host's limiter; null leaves the provider as it is.</param>
    /// <returns>The limited provider.</returns>
    public static ILlmProvider Wrap(ILlmProvider provider, ILlmRateLimiter? rateLimiter)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return rateLimiter is null || provider is RateLimitedLlmProvider || provider.Capabilities.RunsOwnTools
            ? provider
            : new RateLimitedLlmProvider(provider, rateLimiter);
    }

    /// <summary>The provider under the limiter, for type inspection only; <paramref name="provider"/> itself when it is not limited.</summary>
    internal static ILlmProvider Unwrap(ILlmProvider provider) =>
        provider is RateLimitedLlmProvider limited ? limited._inner : provider;

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public LlmConfig? BaseConfig => _inner.BaseConfig;

    /// <inheritdoc />
    /// <remarks>
    /// The wrapped provider's, like <see cref="BaseConfig"/>: a decorator that declared nothing hid
    /// what the provider can do — the echo provider's <c>ReplaysPrompt</c> that the crew's planner
    /// reads (GAP-31), a response format the planner and the judge constrain their replies with.
    /// </remarks>
    public LlmProviderCapabilities Capabilities => _inner.Capabilities;

    /// <inheritdoc />
    public bool SupportsStreaming => (_inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    /// <inheritdoc />
    public async Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken).ConfigureAwait(false);
        s_inLimitedCall.Value = true;
        return await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken).ConfigureAwait(false);
        s_inLimitedCall.Value = true;
        return await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Streaming passthrough: the lease is taken before the first chunk and held for the whole
    /// enumeration — the socket stays open for the stream's lifetime, so releasing it earlier would
    /// let a fan-out exceed <c>MaxConcurrentRequests</c>. The mark is set again each time the stream
    /// resumes in its consumer's context. A non-streaming inner provider falls back to a buffered
    /// single-chunk stream, under the same lease.
    /// </summary>
    /// <remarks>
    /// An unconfigured provider is exactly a non-streaming one — it declares
    /// <c>SupportsStreaming = false</c> — so the fallback is the path its refusal takes, and that
    /// refusal is an empty <c>Content</c> with the reason in the metadata. Yielding nothing would
    /// hand the caller a stream that ended normally on silence; the refusal is thrown instead, in the
    /// same family as the providers' own streaming failures.
    /// </remarks>
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lease = await LeaseAsync(cancellationToken).ConfigureAwait(false);
        s_inLimitedCall.Value = true;
        if (_inner is IStreamingLlmProvider streaming && streaming.SupportsStreaming)
        {
            await foreach (var chunk in streaming.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
            {
                yield return chunk;
                s_inLimitedCall.Value = true;
            }

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
        using var lease = await LeaseAsync(cancellationToken).ConfigureAwait(false);
        s_inLimitedCall.Value = true;
        if (_inner is IStreamingLlmProvider streaming && streaming.SupportsStreaming)
        {
            await foreach (var ev in streaming.ChatStreamingAsync(messages, config, cancellationToken).ConfigureAwait(false))
            {
                yield return ev;
                s_inLimitedCall.Value = true;
            }

            yield break;
        }

        var response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Content))
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    /// <summary>
    /// The call's lease — none for a call made inside another limited call, whose lease covers it.
    /// Read before the caller sets its own mark: an async method's changes to the flow never reach
    /// its caller, so the mark is set by the method that makes the call.
    /// </summary>
    private Task<IDisposable?> LeaseAsync(CancellationToken cancellationToken) =>
        s_inLimitedCall.Value ? Task.FromResult<IDisposable?>(null) : AcquireAsync(cancellationToken);

    private async Task<IDisposable?> AcquireAsync(CancellationToken cancellationToken)
    {
        var acquisition = await _rateLimiter.AcquireAsync(_inner.Name, cancellationToken).ConfigureAwait(false);
        if (acquisition.IsAcquired)
            return acquisition.Lease;

        // The limiter queues what it can and refuses past its QueueLimit: the refusal is retried on
        // its RetryAfter, a bounded number of times, then the call fails with the limiter's reason.
        for (var attempt = 0; attempt < _maxAcquireRetries; attempt++)
        {
            await Task.Delay(acquisition.RetryAfter ?? DefaultRetryDelay, cancellationToken).ConfigureAwait(false);
            acquisition = await _rateLimiter.AcquireAsync(_inner.Name, cancellationToken).ConfigureAwait(false);
            if (acquisition.IsAcquired)
                return acquisition.Lease;
        }

        throw new InvalidOperationException(
            $"The host's RateLimiting refused the call to provider '{_inner.Name}' {_maxAcquireRetries + 1} times: " +
            $"{acquisition.DenialReason}. Raise RateLimiting:GlobalRequestsPerMinute, ProviderRequestsPerMinute, " +
            "MaxConcurrentRequests or QueueLimit, or spread the work.");
    }
}
