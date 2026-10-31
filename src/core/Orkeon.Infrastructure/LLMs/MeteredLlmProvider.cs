using System.Diagnostics;
using System.Runtime.CompilerServices;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Telemetry;
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
/// tokens the meter never saw. <see cref="LlmProviderFactory"/> wraps every provider it builds,
/// <c>AddOrkeonLlmProvider</c> every provider registered by hand, <c>ManagerLlmResolver</c> the
/// provider a C# crew gives its manager (GAP-19) or its manager agent carries (GAP-34),
/// <c>SequentialCrewOrchestrator</c> the one it gives its planner (GAP-33), and
/// <c>LlmProfileRegistry.ForProvider</c> the one an agent carries itself (<c>Agent.Llm</c>, GAP-34),
/// so the chat client adapter, the basic-provider adapter and every direct consumer are covered by
/// the same wrapper, whoever calls. Each of them goes through <see cref="LlmProviderEntrance"/>,
/// which puts the host's limiter around the meter (<see cref="RateLimitedLlmProvider"/>, GAP-38).
/// </para>
/// <para>
/// A provider that reports no usage is estimated and the event says so
/// (<see cref="CostUsageEvent.Estimated"/>); a text-only stream has no usage channel at all
/// and is always estimated, at its end. An estimate never produces a cost: the price is the
/// vendor's own, read from its answer, or nothing (DD-1). A call that failed before any answer
/// is not reported — nothing reached the model's output, and the provider counted nothing.
/// </para>
/// <para>
/// A call is counted once, by the meter nearest the model (GAP-34). A provider may answer through
/// another metered provider: a Microsoft Agent Framework agent built over Orkeon's own model answers
/// for an agent whose client is metered too. Every metered call marks the flow it runs on, and a
/// meter that counts tells the calls around it: an outer call during which an inner meter counted
/// reports nothing, so the event names the real provider and model. Concurrent calls each carry their
/// own mark.
/// </para>
/// </summary>
public sealed class MeteredLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    /// <summary>The metered call in progress on the current flow; null outside any.</summary>
    private static readonly AsyncLocal<MeteredCall?> s_currentCall = new();

    private readonly ILlmProvider _inner;
    private readonly ILlmUsageSink? _sink;

    /// <summary>
    /// One metered call, and whether a meter nearer the model counted during it. Held by the call's
    /// own flow (<see cref="AsyncLocal{T}"/>) and its locals: a call started inside it — even on a
    /// stream that resumes in its consumer's context — reaches it through the reference it captured.
    /// </summary>
    private sealed class MeteredCall
    {
        private int _countedInside;
        private int _reportedInside;

        public MeteredCall(MeteredCall? outer)
        {
            Outer = outer;
            Started = Stopwatch.GetTimestamp();
        }

        /// <summary>The <see cref="Stopwatch"/> timestamp at which the call began.</summary>
        public long Started { get; }

        /// <summary>The metered call this one runs inside, if any.</summary>
        public MeteredCall? Outer { get; }

        /// <summary>Whether a meter inside this call counted it already.</summary>
        public bool CountedInside => Volatile.Read(ref _countedInside) != 0;

        /// <summary>Whether a meter inside this call reported its usage to a sink.</summary>
        public bool ReportedInside => Volatile.Read(ref _reportedInside) != 0;

        /// <summary>Tells every call around this one that a meter inside them counted.</summary>
        public void MarkOuters()
        {
            for (var call = Outer; call is not null; call = call.Outer)
                Interlocked.Exchange(ref call._countedInside, 1);
        }

        /// <summary>Tells every call around this one that a meter inside them reported to a sink.</summary>
        public void MarkOutersReported()
        {
            for (var call = Outer; call is not null; call = call.Outer)
                Interlocked.Exchange(ref call._reportedInside, 1);
        }
    }

    /// <summary>
    /// Opens a metered call on the current flow, inside the one in progress. Called from an async
    /// method or iterator, whose builder restores the caller's context: the mark never leaks out.
    /// </summary>
    private static MeteredCall BeginCall() => s_currentCall.Value = new MeteredCall(s_currentCall.Value);

    private MeteredLlmProvider(ILlmProvider inner, ILlmUsageSink? sink)
    {
        _inner = inner;
        _sink = sink;

        // Reading the meter creates its instruments: a listener sees them before the first call.
        _ = OrkeonMeter.Meter;
    }

    /// <summary>
    /// Meters <paramref name="provider"/> on the Orkeon meter and, when there is a
    /// <paramref name="sink"/>, for it too. Always wraps, except a provider that is already
    /// metered, which is returned as it is — a call is counted once, however many registrations
    /// it went through. Without a sink the wrapper measures for the meter only and publishes no
    /// usage event.
    /// </summary>
    /// <param name="provider">The provider to meter.</param>
    /// <param name="sink">The host's usage receiver; null measures for the meter only.</param>
    /// <returns>The metered provider.</returns>
    public static ILlmProvider Wrap(ILlmProvider provider, ILlmUsageSink? sink)
    {
        ArgumentNullException.ThrowIfNull(provider);
        // A meter without a sink never takes the report from a sink: that one is wrapped around it.
        return UnderEntranceLayers(provider) is MeteredLlmProvider existing && (existing._sink is not null || sink is null)
            ? provider
            : new MeteredLlmProvider(provider, sink);
    }

    /// <summary>The provider under the entrance's limiter and traced layers, the meter still on it.</summary>
    private static ILlmProvider UnderEntranceLayers(ILlmProvider provider)
    {
        var underLimiter = RateLimitedLlmProvider.Unwrap(provider);
        return underLimiter is TracedLlmProvider traced ? traced.Inner : underLimiter;
    }

    /// <summary>
    /// The provider under the meter — and under the host's limiter around it, the two layers an
    /// entrance applies (<see cref="LlmProviderEntrance"/>) — for type inspection only: a caller that
    /// must know which vendor it talks to (the Anthropic tool-call parser, the retry observer).
    /// Calling it directly would bypass the meter and the limiter.
    /// </summary>
    /// <param name="provider">A provider, metered or not, limited or not.</param>
    /// <returns>The provider the meter wraps, or <paramref name="provider"/> itself.</returns>
    public static ILlmProvider Unwrap(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var underMeter = UnderMeter(provider);
        return underMeter is CallMode.CallModeLlmProvider callMode ? callMode.Inner : underMeter;
    }

    /// <summary>The provider under the limiter and the meter, the call-mode decorator included.</summary>
    internal static ILlmProvider UnderMeter(ILlmProvider provider)
    {
        var underLayers = UnderEntranceLayers(provider);
        return underLayers is MeteredLlmProvider metered ? metered._inner : underLayers;
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
    public bool StreamsNatively => (_inner as IStreamingLlmProvider)?.StreamsNatively ?? false;

    /// <inheritdoc />
    public async Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var attribution = LlmUsageScope.Current;
        var call = BeginCall();
        LlmResponse response;
        try
        {
            response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MeasureFailure(call, config, ex);
            throw;
        }

        Report(response, attribution, new Prompt(prompt, null), call);
        return response;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var attribution = LlmUsageScope.Current;
        var call = BeginCall();
        LlmResponse response;
        try
        {
            response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MeasureFailure(call, config, ex);
            throw;
        }

        Report(response, attribution, new Prompt(null, messages), call);
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
        var call = BeginCall();

        if (_inner is not IStreamingLlmProvider streaming)
        {
            LlmResponse response;
            try
            {
                response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MeasureFailure(call, config, ex);
                throw;
            }

            Report(response, attribution, new Prompt(prompt, null), call);
            if (!string.IsNullOrEmpty(response.Content))
                yield return response.Content;
            else if (response.Error is { } error)
                throw new HttpRequestException($"{error}: the buffered fallback answered with no content, so the stream carries nothing.");
            yield break;
        }

        long characters = 0;
        var ended = false;
        var failed = false;
        IAsyncEnumerator<string> chunks;
        try
        {
            chunks = streaming.GenerateStreamingAsync(prompt, config, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            MeasureFailure(call, config, ex);
            throw;
        }

        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await chunks.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failed = true;
                    MeasureFailure(call, config, ex);
                    throw;
                }

                if (!more)
                    break;

                var chunk = chunks.Current;
                characters += chunk.Length;
                yield return chunk;
            }

            ended = true;
        }
        finally
        {
            // A stream that ran to its end was answered, even with nothing; one that broke
            // off counts only if something came back — a refused request streamed nothing.
            // A stream that threw is measured once, as a failure.
            if (!failed && (ended || characters > 0))
                ReportStreamEstimate(attribution, LlmUsageEstimator.Prompt(prompt), characters, config, call);

            await DisposeKeepingFailureAsync(chunks, failed).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes a stream's enumerator. When the stream already failed, a disposal that throws
    /// too is dropped: the caller gets the original exception, not the one from the clean-up.
    /// </summary>
    private static async ValueTask DisposeKeepingFailureAsync(IAsyncDisposable enumerator, bool failed)
    {
        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // the clean-up failure is dropped on purpose: the stream's own exception is the one that matters
        catch (Exception) when (failed)
#pragma warning restore CA1031
        {
            // Dropped: the stream already failed, and its exception is the one the caller gets.
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
        var call = BeginCall();

        if (_inner is not IStreamingLlmProvider streaming)
        {
            await foreach (var buffered in BufferedChatEventsAsync(messages, config, attribution, call, cancellationToken).ConfigureAwait(false))
                yield return buffered;
            yield break;
        }

        long characters = 0;
        var reported = false;
        var ended = false;
        var failed = false;
        var events = OpenStream(streaming, messages, config, call, cancellationToken);

        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await events.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failed = true;
                    MeasureStreamFailure(reported, call, config, ex);
                    throw;
                }

                if (!more)
                    break;

                var streamed = events.Current;
                if (!reported && streamed is { Kind: LlmStreamEventKind.Completed, FinalResponse: { } final })
                {
                    // Before the consumer sees the end: the meter moves with the answer.
                    reported = true;
                    Report(final, attribution, new Prompt(null, messages), call);
                }
                else
                {
                    characters += StreamedCharacters(streamed);
                }

                yield return streamed;
            }

            ended = true;
        }
        finally
        {
            // No final response to read: the stream is counted on what it delivered.
            if (NeedsStreamEstimate(failed, reported, ended, characters))
                ReportStreamEstimate(attribution, LlmUsageEstimator.Prompt(messages), characters, config, call);

            await DisposeKeepingFailureAsync(events, failed).ConfigureAwait(false);
        }
    }

    /// <summary>Measures a stream that threw; a final response already measured the call, so a later throw adds nothing.</summary>
    private void MeasureStreamFailure(bool reported, MeteredCall call, LlmConfig? config, Exception failure)
    {
        if (!reported)
            MeasureFailure(call, config, failure);
    }

    /// <summary>Whether a stream that read no final response is counted on what it delivered: it ended, or it streamed something, and did not fail.</summary>
    private static bool NeedsStreamEstimate(bool failed, bool reported, bool ended, long characters)
        => !failed && !reported && (ended || characters > 0);

    /// <summary>Chat streaming over a provider that does not stream: one buffered call, told as events.</summary>
    private async IAsyncEnumerable<LlmStreamEvent> BufferedChatEventsAsync(
        LlmMessage[] messages, LlmConfig? config, LlmUsageAttribution attribution, MeteredCall call,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LlmResponse response;
        try
        {
            response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MeasureFailure(call, config, ex);
            throw;
        }

        Report(response, attribution, new Prompt(null, messages), call);
        if (!string.IsNullOrEmpty(response.Content))
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    /// <summary>Opens the inner stream; a provider that throws on opening it is measured as a failed call.</summary>
    private IAsyncEnumerator<LlmStreamEvent> OpenStream(
        IStreamingLlmProvider streaming, LlmMessage[] messages, LlmConfig? config, MeteredCall call, CancellationToken cancellationToken)
    {
        try
        {
            return streaming.ChatStreamingAsync(messages, config, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            MeasureFailure(call, config, ex);
            throw;
        }
    }

    /// <summary>The characters a content or reasoning delta carries; none for any other event.</summary>
    private static long StreamedCharacters(LlmStreamEvent streamed) =>
        streamed.Kind is LlmStreamEventKind.ContentDelta or LlmStreamEventKind.ReasoningDelta
            ? streamed.Delta?.Length ?? 0
            : 0;

    /// <summary>The bounded <c>error.type</c> of an answer that carries an error and no usage.</summary>
    private const string ProviderErrorType = "provider_error";

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
    /// the sum every budget aggregates. A call a meter inside it counted already reports nothing.
    /// </summary>
    private void Report(LlmResponse? response, LlmUsageAttribution attribution, Prompt prompt, MeteredCall call)
    {
        if (response is null || call.ReportedInside)
            return;

        var counted = LlmUsageEstimator.Reported(response);
        var model = response.Model ?? NamedModel(_inner.BaseConfig) ?? string.Empty;

        // A call that failed before any answer: the provider counted nothing and returned
        // nothing, and an estimate of the prompt would count a request that never ran.
        if (!counted && response.Error is not null)
        {
            MeasureOnce(call, model, ProviderErrorType, null, null);
            return;
        }

        var (promptTokens, completionTokens) = TokensOf(response, counted, prompt);
        var billed = LlmVendorCost.TryRead(response, out var cost, out var currency);

        if (!call.CountedInside)
        {
            Measure(call, model, null, counted ? promptTokens : null, counted ? completionTokens : null);
            AddUsdCost(billed, cost, currency);
        }

        if (_sink is null)
            return;

        call.MarkOutersReported();

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
            CacheWriteTokens = response.CacheWriteTokens,
            CacheWrite1hTokens = response.CacheWrite1hTokens ?? 0,
            ReasoningTokens = ReasoningTokensOf(response),
            Estimated = !counted,
            // What the vendor billed, as billed — null when it billed nothing in its answer,
            // never a price computed here (DD-1).
            Cost = billed ? cost : null,
            CostCurrency = billed ? currency : null,
        });
    }

    /// <summary>The tokens of an answer: as the provider counted them, else estimated.</summary>
    private static (int Prompt, int Completion) TokensOf(LlmResponse response, bool counted, Prompt prompt)
    {
        if (!counted)
            return (prompt.EstimateTokens(), LlmUsageEstimator.Completion(response));

        var promptTokens = response.PromptTokens ?? 0;
        return (promptTokens, response.CompletionTokens ?? Math.Max(0, response.TokensUsed - promptTokens));
    }

    /// <summary>
    /// The reasoning tokens the provider reported in its metadata (<see cref="LlmUsageMetadataKeys.ReasoningTokens"/>);
    /// null when it reported none or a value that is not a count.
    /// </summary>
    private static long? ReasoningTokensOf(LlmResponse response) =>
        response.Metadata.TryGetValue(LlmUsageMetadataKeys.ReasoningTokens, out var value)
            ? value switch
            {
                int i => i,
                long l => l,
                double d when d >= 0 && d <= long.MaxValue => (long)d,
                decimal m when m >= 0 && m <= long.MaxValue => (long)m,
                _ => null,
            }
            : null;

    /// <summary>Adds a billed cost to the meter when the vendor billed it in US dollars.</summary>
    private static void AddUsdCost(bool billed, decimal cost, string? currency)
    {
        if (billed && string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            OrkeonMeter.AddCostUsd((double)cost);
    }

    /// <summary>
    /// Reports a stream that delivered no usage: both sides estimated — the prompt from what
    /// was sent, the completion from the characters received — and never a cost. A stream a meter
    /// inside it counted already reports nothing.
    /// </summary>
    private void ReportStreamEstimate(LlmUsageAttribution attribution, int promptTokens, long characters, LlmConfig? config, MeteredCall call)
    {
        if (call.ReportedInside)
            return;

        // No answer names the model on this path: the one asked for is the best reading —
        // and a call that names none runs on the provider's own (GAP-18).
        var model = RequestedModel(config);
        MeasureOnce(call, model, null, null, null);
        if (_sink is null)
            return;

        call.MarkOutersReported();

        Record(new CostUsageEvent
        {
            CrewId = attribution.CrewId,
            AgentId = attribution.AgentId,
            TaskId = attribution.TaskId,
            OperationType = attribution.Operation,
            Model = model,
            Provider = _inner.Name,
            PromptTokens = promptTokens,
            CompletionTokens = (int)Math.Min(int.MaxValue, LlmUsageEstimator.FromCharacterCount(characters)),
            Estimated = true,
        });
    }

    private static string? NamedModel(LlmConfig? config) =>
        string.IsNullOrWhiteSpace(config?.Model) ? null : config.Model;

    private string RequestedModel(LlmConfig? config) =>
        NamedModel(config) ?? NamedModel(_inner.BaseConfig) ?? string.Empty;

    /// <summary>
    /// Measures one call on the Orkeon meter and tells the calls around <paramref name="call"/>
    /// that it was counted, so a meter nearer the caller never measures it again.
    /// </summary>
    private void Measure(MeteredCall call, string model, string? errorType, long? inputTokens, long? outputTokens)
    {
        call.MarkOuters();
        OrkeonMeter.RecordLlmCall(
            _inner.Name, model, Stopwatch.GetElapsedTime(call.Started).TotalSeconds, errorType, inputTokens, outputTokens);
    }

    /// <summary>Measures the call unless a meter inside it measured it already.</summary>
    private void MeasureOnce(MeteredCall call, string model, string? errorType, long? inputTokens, long? outputTokens)
    {
        if (!call.CountedInside)
            Measure(call, model, errorType, inputTokens, outputTokens);
    }

    /// <summary>Measures a call that threw, unless a meter inside it measured it already.</summary>
    private void MeasureFailure(MeteredCall call, LlmConfig? config, Exception failure)
    {
        if (!call.CountedInside)
            Measure(call, RequestedModel(config), failure.GetType().FullName ?? failure.GetType().Name, null, null);
    }

    /// <summary>
    /// Hands one event to the sink. A sink that throws degrades to unobserved usage, never to a
    /// failed call: the meter must not break what it measures.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-sink fault barrier: usage accounting must never fail the LLM call it observes.")]
    private void Record(CostUsageEvent usage)
    {
        try
        {
            _sink!.Record(usage);
        }
        catch (Exception)
        {
            // Deliberately swallowed — see the fault-barrier contract above.
        }
    }
}
