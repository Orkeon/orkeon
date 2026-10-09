using System.Globalization;
using System.Runtime.CompilerServices;
using Orkeon.Domain.Constants.Resilience;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.Resilience;

namespace Orkeon.Infrastructure.LLMs.Base;

/// <summary>
/// The bounds of a streamed call. A buffered call runs whole under <c>HttpClient.Timeout</c>;
/// a streamed one is sent with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, and that
/// timeout stops counting once the headers are in: the body was read on the caller's token
/// alone, so a model that thinks for minutes before its first token — GLM, Kimi, DeepSeek do by
/// default — hung a Studio run past any <c>Llm:TimeoutSeconds</c>, and a stream the provider
/// closed without an answer came back as an empty answer, which the agent loop blamed on
/// <c>Llm:MaxTokens</c> and retried without tools (LLM-12; the buffered half is LLM-11).
/// </summary>
public abstract partial class HttpLlmProviderBase
{
    /// <summary>Which bound of a <see cref="StreamReadBudget"/> elapsed.</summary>
    protected enum StreamBudgetKind
    {
        /// <summary><c>Llm:TimeoutSeconds</c>: the whole call, headers and body.</summary>
        Total,

        /// <summary><c>Llm:StreamIdleSeconds</c>: the silence between two lines of the body.</summary>
        Idle,
    }

    /// <summary>
    /// One streamed call's budget: <c>Llm:TimeoutSeconds</c> over the whole call (headers and
    /// body, the meaning it has on a buffered call) and, when set, <c>Llm:StreamIdleSeconds</c>
    /// between two lines of the body. Started before the request is sent, read by the bounded
    /// line readers, disposed with the call.
    /// </summary>
    protected sealed class StreamReadBudget : IDisposable
    {
        private readonly CancellationToken _caller;
        private readonly CancellationTokenSource _total;
        private readonly CancellationTokenSource? _idle;

        private StreamReadBudget(LlmConfig effectiveConfig, CancellationToken callerToken)
        {
            _caller = callerToken;
            TotalSeconds = effectiveConfig.ResolveTimeoutSeconds();
            IdleSeconds = effectiveConfig.StreamIdleSeconds;
            _total = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            _total.CancelAfter(TimeSpan.FromSeconds(TotalSeconds));
            if (IdleSeconds is { } idle && idle > 0)
                _idle = CancellationTokenSource.CreateLinkedTokenSource(_total.Token);
        }

        /// <summary>Starts the budget of one call: the clock of <c>Llm:TimeoutSeconds</c> runs from here.</summary>
        /// <param name="effectiveConfig">The configuration of the call, for its two bounds.</param>
        /// <param name="callerToken">The caller's own cancellation, which keeps propagating as such.</param>
        public static StreamReadBudget Start(LlmConfig effectiveConfig, CancellationToken callerToken)
        {
            ArgumentNullException.ThrowIfNull(effectiveConfig);
            return new StreamReadBudget(effectiveConfig, callerToken);
        }

        /// <summary>The caller's token and the total bound, linked: what the request is sent with.</summary>
        public CancellationToken Token => _total.Token;

        /// <summary><c>Llm:TimeoutSeconds</c> as resolved for this call.</summary>
        public int TotalSeconds { get; }

        /// <summary><c>Llm:StreamIdleSeconds</c>, or null when nothing bounds the silence.</summary>
        public int? IdleSeconds { get; }

        /// <summary>The bound that elapsed, or null while none has (or the caller cancelled).</summary>
        public StreamBudgetKind? Elapsed { get; private set; }

        /// <summary>Whether the body's own terminator (<c>data: [DONE]</c>) was read.</summary>
        public bool SawTerminator { get; set; }

        /// <summary>How many characters of body were read, for the failure sentence.</summary>
        public long CharactersStreamed { get; private set; }

        /// <summary>
        /// The token one read of the body runs on: the idle bound re-armed for <see cref="IdleSeconds"/>,
        /// else the total bound alone.
        /// </summary>
        public CancellationToken ArmIdle()
        {
            if (_idle is null)
                return _total.Token;

            _idle.CancelAfter(TimeSpan.FromSeconds(IdleSeconds!.Value));
            return _idle.Token;
        }

        /// <summary>Counts one line of the body as read.</summary>
        public void LineRead(string line)
        {
            ArgumentNullException.ThrowIfNull(line);
            CharactersStreamed += line.Length;
        }

        /// <summary>
        /// Records which bound <paramref name="exception"/> comes from, and says whether it is one of
        /// this budget's: false for the caller's own cancellation, which must keep propagating.
        /// An <c>HttpClient.Timeout</c> that elapsed on the headers phase (the same value, started a
        /// moment earlier) counts as the total bound.
        /// </summary>
        public bool TryMarkElapsed(OperationCanceledException exception)
        {
            if (_caller.IsCancellationRequested)
                return false;

            if (_total.IsCancellationRequested || ResiliencePolicies.IsHttpClientTimeout(exception))
            {
                Elapsed = StreamBudgetKind.Total;
                return true;
            }

            if (_idle is { IsCancellationRequested: true })
            {
                Elapsed = StreamBudgetKind.Idle;
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _idle?.Dispose();
            _total.Dispose();
        }
    }

    /// <summary>
    /// Sends a streaming request under <paramref name="budget"/>. Returns null when the total bound
    /// elapsed before the headers arrived (<see cref="StreamReadBudget.Elapsed"/> says so); the
    /// caller's own cancellation keeps propagating.
    /// </summary>
    protected Task<HttpResponseMessage?> TrySendStreamingRequestAsync(
        HttpClient client,
        Uri endpoint,
        string jsonPayload,
        StreamReadBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return TrySendCoreAsync();

        async Task<HttpResponseMessage?> TrySendCoreAsync()
        {
            try
            {
                return await SendStreamingRequestAsync(client, endpoint, jsonPayload, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (budget.TryMarkElapsed(ex))
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Reads an SSE stream under <paramref name="budget"/>: the data payloads until
    /// <c>data: [DONE]</c> (recorded as <see cref="StreamReadBudget.SawTerminator"/>), the
    /// connection's end, or an elapsed bound (<see cref="StreamReadBudget.Elapsed"/>), which ends
    /// the sequence normally so the dialect can finish its answer with the failure in it.
    /// </summary>
    protected static async IAsyncEnumerable<string> ReadSseStreamAsync(
        HttpResponseMessage response,
        StreamReadBudget budget,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        await foreach (var line in ReadStreamLinesAsync(response, budget).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!line.StartsWith(HttpDefaults.SseDataPrefix, StringComparison.Ordinal))
                continue;

            var data = line[HttpDefaults.SseDataPrefix.Length..];

            if (data == HttpDefaults.SseDoneMarker)
            {
                budget.SawTerminator = true;
                yield break;
            }

            yield return data;
        }
    }

    /// <summary>Reads an NDJSON stream (Ollama) under <paramref name="budget"/>; see <see cref="ReadSseStreamAsync(HttpResponseMessage, StreamReadBudget, CancellationToken)"/>.</summary>
    protected static IAsyncEnumerable<string> ReadNdjsonStreamAsync(HttpResponseMessage response, StreamReadBudget budget)
        => ReadStreamLinesAsync(response, budget);

    /// <summary>
    /// Reads the non-empty lines of a response body under <paramref name="budget"/>. An elapsed
    /// bound ends the sequence (the budget records which one); the caller's own cancellation
    /// propagates. Cancelling the read closes the connection: nothing after it is read.
    /// </summary>
    protected static IAsyncEnumerable<string> ReadStreamLinesAsync(HttpResponseMessage response, StreamReadBudget budget)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(budget);
        return ReadBoundedLinesAsync(response, budget);
    }

    private static async IAsyncEnumerable<string> ReadBoundedLinesAsync(HttpResponseMessage response, StreamReadBudget budget)
    {
        Stream? stream = null;
        try
        {
            stream = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (budget.TryMarkElapsed(ex))
        {
            // The total bound elapsed before the body opened.
        }

        if (stream is null)
            yield break;

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(budget.ArmIdle()).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (budget.TryMarkElapsed(ex))
                {
                    line = null;
                }

                if (line is null)
                    yield break;

                budget.LineRead(line);
                if (line.Length == 0)
                    continue;

                yield return line;
            }
        }
    }

    /// <summary>
    /// What an elapsed <c>Llm:StreamIdleSeconds</c> means, said where the reader will act on it —
    /// the sibling of <see cref="TimeoutFailureMessage"/>.
    /// </summary>
    /// <param name="providerDisplayName">The provider's display name.</param>
    /// <param name="idleSeconds">The idle bound that elapsed, in seconds.</param>
    /// <param name="charactersStreamed">How much of the body had arrived before the silence.</param>
    protected static string StreamIdleFailureMessage(string providerDisplayName, int idleSeconds, long charactersStreamed) =>
        $"{providerDisplayName} wrote nothing to the stream for Llm:StreamIdleSeconds = {idleSeconds} s " +
        $"({charactersStreamed.ToString(CultureInfo.InvariantCulture)} characters had arrived), so this is a failed call, " +
        "not an empty answer. A model that thinks before it writes may stay silent for a while — raise " +
        "Llm:StreamIdleSeconds, leave it unset to bound the call by Llm:TimeoutSeconds alone, or turn thinking off.";

    /// <summary>
    /// What a stream closed before any answer means: the provider ended the body without its
    /// finish marker and nothing of an answer had arrived. A failed call, not an empty answer.
    /// </summary>
    /// <param name="providerDisplayName">The provider's display name.</param>
    /// <param name="reasoningCharacters">How much reasoning the model streamed before the end, if any.</param>
    protected static string StreamTruncatedFailureMessage(string providerDisplayName, int reasoningCharacters) =>
        $"{providerDisplayName} closed the stream before any answer arrived (no finish marker" +
        (reasoningCharacters > 0
            ? $"; {reasoningCharacters.ToString(CultureInfo.InvariantCulture)} characters of reasoning were streamed and no answer text)"
            : ")") +
        ": a failed call, not an empty answer. The connection was cut on the provider's side or by a gateway " +
        "on the way — retry, and if it repeats with a model that thinks before it writes, turn thinking off.";

    /// <summary>
    /// The sentence a streamed call failed with, or null when it did not: an elapsed bound
    /// (<see cref="StreamBudgetKind.Total"/>, <see cref="StreamBudgetKind.Idle"/>), else a stream
    /// closed without its finish marker before any answer. A stream closed without its marker
    /// after some answer is not a failure — see <see cref="ApplyStreamOutcome"/>.
    /// </summary>
    protected static string? StreamFailureMessage(
        StreamReadBudget budget, bool finished, bool hasAnswer, int reasoningCharacters, string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return budget.Elapsed switch
        {
            StreamBudgetKind.Total => TimeoutFailureMessage(providerDisplayName, budget.TotalSeconds),
            StreamBudgetKind.Idle => StreamIdleFailureMessage(providerDisplayName, budget.IdleSeconds ?? 0, budget.CharactersStreamed),
            _ when !finished && !budget.SawTerminator && !hasAnswer => StreamTruncatedFailureMessage(providerDisplayName, reasoningCharacters),
            _ => null,
        };
    }

    /// <summary>The <c>error_type</c> that goes with <see cref="StreamFailureMessage"/>.</summary>
    protected static string StreamFailureType(StreamReadBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return budget.Elapsed switch
        {
            StreamBudgetKind.Total => nameof(TaskCanceledException),
            StreamBudgetKind.Idle => StreamIdleTimeoutErrorType,
            _ => StreamTruncatedErrorType,
        };
    }

    /// <summary>The <c>error_type</c> of an elapsed <c>Llm:StreamIdleSeconds</c>.</summary>
    protected const string StreamIdleTimeoutErrorType = "StreamIdleTimeout";

    /// <summary>The <c>error_type</c> of a stream closed before any answer.</summary>
    protected const string StreamTruncatedErrorType = "StreamTruncated";

    /// <summary>
    /// Writes the outcome of a streamed call into its response metadata: <c>error</c> and
    /// <c>error_type</c> for a failed call (an elapsed bound, a stream closed before any answer),
    /// the <see cref="LlmResponseMetadataKeys.StreamTruncated"/> flag for a stream closed without
    /// its finish marker after some answer. Returns true when the call failed: the caller then
    /// serves no <c>RawResponseBody</c>, since nothing in a failed stream is an instruction.
    /// </summary>
    /// <param name="metadata">The response's metadata under construction.</param>
    /// <param name="budget">The budget the call ran under.</param>
    /// <param name="finished">Whether the dialect read its own finish marker.</param>
    /// <param name="hasAnswer">Whether any answer text or tool call arrived.</param>
    /// <param name="reasoningCharacters">How much reasoning was streamed.</param>
    /// <param name="providerDisplayName">The provider's display name.</param>
    protected static bool ApplyStreamOutcome(
        LlmResponseMetadata.Builder metadata,
        StreamReadBudget budget,
        bool finished,
        bool hasAnswer,
        int reasoningCharacters,
        string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(budget);

        if (StreamFailureMessage(budget, finished, hasAnswer, reasoningCharacters, providerDisplayName) is { } failure)
        {
            metadata.AddError(failure).AddErrorType(StreamFailureType(budget));
            return true;
        }

        if (!finished && !budget.SawTerminator)
            metadata.Add(LlmResponseMetadataKeys.StreamTruncated, true);

        return false;
    }

    /// <summary>
    /// How many times the headers phase of a streamed call is re-sent after an elapsed
    /// <c>HttpClient.Timeout</c>: the buffered path's budget
    /// (<see cref="ResilienceDefaults.LlmTimeoutRetries"/>), none when retries are off.
    /// </summary>
    private int StreamingTimeoutRetries => Config.MaxRetries > 0 ? ResilienceDefaults.LlmTimeoutRetries : 0;
}
