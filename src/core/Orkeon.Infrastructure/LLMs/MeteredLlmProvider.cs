using System.Runtime.CompilerServices;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.CostTracking;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// The one place LLM usage is measured (STUDIO-42 D-01): a decorator that reports every
/// generation call of the provider it wraps to the host's <see cref="ILlmUsageSink"/>, stamped
/// with the attribution in effect when the call started (<see cref="LlmUsageScope"/>).
/// <para>
/// It used to be the callers' job. Two of them did it — the agent loop and the scripting
/// facade — and every other caller, from the hierarchical manager to the RAG pipelines, spent
/// tokens the meter never saw. <see cref="LlmProviderFactory"/> wraps every provider it builds
/// and <c>AddOrkeonLlmProvider</c> every provider registered by hand, so the chat client
/// adapter, the basic-provider adapter and every direct consumer are covered by the same
/// wrapper, whoever calls.
/// </para>
/// <para>
/// A provider that reports no usage is estimated and the event says so
/// (<see cref="CostUsageEvent.Estimated"/>); a text-only stream has no usage channel at all
/// and is always estimated, at its end. An estimate never produces a cost: the price is the
/// vendor's own, read from its answer, or nothing (DD-1). A call that failed before any answer
/// is not reported — nothing reached the model's output, and the provider counted nothing.
/// </para>
/// </summary>
public sealed class MeteredLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    private readonly ILlmProvider _inner;
    private readonly ILlmUsageSink _sink;

    private MeteredLlmProvider(ILlmProvider inner, ILlmUsageSink sink)
    {
        _inner = inner;
        _sink = sink;
    }

    /// <summary>
    /// Meters <paramref name="provider"/> for <paramref name="sink"/>. Returns the provider
    /// itself when there is no sink (nobody listens, nothing changes) or when it is already
    /// metered — a call is counted once, however many registrations it went through.
    /// </summary>
    /// <param name="provider">The provider to meter.</param>
    /// <param name="sink">The host's usage receiver; null leaves the provider as it is.</param>
    /// <returns>The metered provider.</returns>
    public static ILlmProvider Wrap(ILlmProvider provider, ILlmUsageSink? sink)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return sink is null || provider is MeteredLlmProvider
            ? provider
            : new MeteredLlmProvider(provider, sink);
    }

    /// <summary>
    /// The provider under the meter, for type inspection only — a caller that must know which
    /// vendor it talks to (the Anthropic tool-call parser, the retry observer). Calling it
    /// directly would bypass the meter.
    /// </summary>
    /// <param name="provider">A provider, metered or not.</param>
    /// <returns>The provider the meter wraps, or <paramref name="provider"/> itself.</returns>
    public static ILlmProvider Unwrap(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider is MeteredLlmProvider metered ? metered._inner : provider;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public LlmConfig? BaseConfig => _inner.BaseConfig;

    /// <inheritdoc />
    public LlmProviderCapabilities Capabilities => _inner.Capabilities;

    /// <inheritdoc />
    public bool SupportsStreaming => (_inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    /// <inheritdoc />
    public async Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var attribution = LlmUsageScope.Current;
        var response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        Report(response, attribution, new Prompt(prompt, null));
        return response;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var attribution = LlmUsageScope.Current;
        var response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        Report(response, attribution, new Prompt(null, messages));
        return response;
    }

    /// <summary>
    /// Text-only streaming: the chunks carry no usage, so the call is counted once, at the end
    /// of the stream, as an estimate over what was sent and what came back. A consumer that
    /// stops early still paid for what it received, and that is what is counted.
    /// </summary>
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Read while the consumer's first MoveNextAsync runs in its own scope: after a yield,
        // this iterator resumes in whatever context the consumer is in at the time.
        var attribution = LlmUsageScope.Current;

        if (_inner is not IStreamingLlmProvider streaming)
        {
            var response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            Report(response, attribution, new Prompt(prompt, null));
            if (!string.IsNullOrEmpty(response.Content))
                yield return response.Content;
            else if (response.Error is { } error)
                throw new HttpRequestException($"{error}: the buffered fallback answered with no content, so the stream carries nothing.");
            yield break;
        }

        long characters = 0;
        var ended = false;
        try
        {
            await foreach (var chunk in streaming.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
            {
                characters += chunk.Length;
                yield return chunk;
            }

            ended = true;
        }
        finally
        {
            // A stream that ran to its end was answered, even with nothing; one that broke
            // off counts only if something came back — a refused request streamed nothing.
            if (ended || characters > 0)
                ReportStreamEstimate(attribution, prompt, characters, config);
        }
    }

    /// <summary>
    /// Chat streaming: the call is counted once, when the stream delivers its final response —
    /// with the usage that response carries, or an estimate when it carries none. A stream that
    /// ends without one (abandoned, broken) is counted on what it had streamed so far.
    /// </summary>
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var attribution = LlmUsageScope.Current;

        if (_inner is not IStreamingLlmProvider streaming)
        {
            var response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
            Report(response, attribution, new Prompt(null, messages));
            if (!string.IsNullOrEmpty(response.Content))
                yield return LlmStreamEvent.Content(response.Content);
            yield return LlmStreamEvent.Complete(response);
            yield break;
        }

        long characters = 0;
        var reported = false;
        var ended = false;
        try
        {
            await foreach (var streamed in streaming.ChatStreamingAsync(messages, config, cancellationToken).ConfigureAwait(false))
            {
                if (streamed is { Kind: LlmStreamEventKind.Completed, FinalResponse: { } final } && !reported)
                {
                    // Before the consumer sees the end: the meter moves with the answer.
                    reported = true;
                    Report(final, attribution, new Prompt(null, messages));
                }
                else if (streamed.Kind is LlmStreamEventKind.ContentDelta or LlmStreamEventKind.ReasoningDelta)
                {
                    characters += streamed.Delta?.Length ?? 0;
                }

                yield return streamed;
            }

            ended = true;
        }
        finally
        {
            // No final response to read: the stream is counted on what it delivered.
            if (!reported && (ended || characters > 0))
                ReportStreamEstimate(attribution, messages, characters, config);
        }
    }

    /// <summary>What was sent: a single prompt, or a conversation.</summary>
    private readonly record struct Prompt(string? Text, IReadOnlyList<LlmMessage>? Messages)
    {
        public int EstimateTokens() => Messages is not null
            ? LlmUsageEstimator.Prompt(Messages)
            : LlmUsageEstimator.Prompt(Text);
    }

    /// <summary>
    /// Reports one answered call. A provider that counted is taken at its word; one that said
    /// nothing is estimated and marked. Providers that report only a grand total leave the
    /// split null: the remainder keeps <c>PromptTokens + CompletionTokens == TokensUsed</c>,
    /// the sum every budget aggregates.
    /// </summary>
    private void Report(LlmResponse? response, LlmUsageAttribution attribution, Prompt prompt)
    {
        if (response is null)
            return;

        var counted = LlmUsageEstimator.Reported(response);

        // A call that failed before any answer: the provider counted nothing and returned
        // nothing, and an estimate of the prompt would count a request that never ran.
        if (!counted && response.Error is not null)
            return;

        var promptTokens = counted ? response.PromptTokens ?? 0 : prompt.EstimateTokens();
        var completionTokens = counted
            ? response.CompletionTokens ?? Math.Max(0, response.TokensUsed - promptTokens)
            : LlmUsageEstimator.Completion(response);
        var billed = LlmVendorCost.TryRead(response, out var cost, out var currency);

        Record(new CostUsageEvent
        {
            CrewId = attribution.CrewId,
            AgentId = attribution.AgentId,
            TaskId = attribution.TaskId,
            OperationType = attribution.Operation,
            Model = response.Model ?? string.Empty,
            Provider = _inner.Name,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            // A partition of PromptTokens, never additive to it — null when unreported.
            CacheHitTokens = response.CacheHitTokens,
            CacheMissTokens = response.CacheMissTokens,
            Estimated = !counted,
            // What the vendor billed, as billed — null when it billed nothing in its answer,
            // never a price computed here (DD-1).
            Cost = billed ? cost : null,
            CostCurrency = billed ? currency : null,
        });
    }

    private void ReportStreamEstimate(LlmUsageAttribution attribution, string prompt, long characters, LlmConfig? config)
        => ReportStreamEstimate(attribution, LlmUsageEstimator.Prompt(prompt), characters, config);

    private void ReportStreamEstimate(LlmUsageAttribution attribution, IReadOnlyList<LlmMessage> messages, long characters, LlmConfig? config)
        => ReportStreamEstimate(attribution, LlmUsageEstimator.Prompt(messages), characters, config);

    /// <summary>
    /// Reports a stream that delivered no usage: both sides estimated — the prompt from what
    /// was sent, the completion from the characters received — and never a cost.
    /// </summary>
    private void ReportStreamEstimate(LlmUsageAttribution attribution, int promptTokens, long characters, LlmConfig? config)
        => Record(new CostUsageEvent
        {
            CrewId = attribution.CrewId,
            AgentId = attribution.AgentId,
            TaskId = attribution.TaskId,
            OperationType = attribution.Operation,
            // No answer names the model on this path: the one asked for is the best reading.
            Model = config?.Model ?? _inner.BaseConfig?.Model ?? string.Empty,
            Provider = _inner.Name,
            PromptTokens = promptTokens,
            CompletionTokens = (int)Math.Min(int.MaxValue, LlmUsageEstimator.FromCharacterCount(characters)),
            Estimated = true,
        });

    /// <summary>
    /// Hands one event to the sink. A sink that throws degrades to unobserved usage, never to
    /// a failed call: the meter must not break what it measures.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-sink fault barrier: usage accounting must never fail the LLM call it observes.")]
    private void Record(CostUsageEvent usage)
    {
        try
        {
            _sink.Record(usage);
        }
        catch (Exception)
        {
            // Deliberately swallowed — see the fault-barrier contract above.
        }
    }
}
