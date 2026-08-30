using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// One mode of the LLM provider test protocol (matrix §5).
/// </summary>
/// <remarks>
/// The identifiers are the matrix's own, so a probe result can be transcribed into it without
/// a translation step — the whole point of the exercise is to know what was <em>really</em>
/// exercised, and an approximate mapping would defeat that.
/// </remarks>
internal enum LlmProbeMode
{
    /// <summary>M1 — a single prompt returns non-empty content.</summary>
    M1,

    /// <summary>M2 — a multi-turn conversation with a system message is honoured.</summary>
    M2,

    /// <summary>M3 — single-prompt token streaming yields more than one chunk.</summary>
    M3,

    /// <summary>M4 — chat streaming yields typed events and a terminal Completed.</summary>
    M4,

    /// <summary>M5 — a native tool call is emitted, parsed, and its result accepted back.</summary>
    M5,

    /// <summary>M6 — the text tool-call protocol is emitted and read by the fallback parser.</summary>
    M6,

    /// <summary>M7 — a thinking configuration is accepted and, where applicable, traced.</summary>
    M7,

    /// <summary>M8 — a response-format constraint yields parseable JSON.</summary>
    M8,

    /// <summary>M9 — an image part is accepted and actually looked at.</summary>
    M9,

    /// <summary>M10 — a repeated prompt prefix is served from the provider's cache.</summary>
    M10,

    /// <summary>M12 — an invalid model produces a typed error, not an exception.</summary>
    M12,

    /// <summary>M13 — a cancelled call stops promptly without throwing to the caller.</summary>
    M13,
}

/// <summary>How one mode ended.</summary>
/// <remarks>
/// A mode a provider cannot possibly satisfy is not a failure, and calling it one would make
/// every campaign report read red for reasons nobody can act on. It gets its own outcome so a
/// reader can tell "this provider has no vision API" from "this provider's vision API broke".
/// </remarks>
internal enum LlmProbeOutcome
{
    /// <summary>The provider behaved as the mode requires.</summary>
    Passed,

    /// <summary>The provider did not behave as the mode requires — actionable.</summary>
    Failed,

    /// <summary>The mode does not apply to this provider or model; nothing was exercised.</summary>
    NotApplicable,
}

/// <summary>Outcome of one probe mode.</summary>
/// <param name="Mode">The protocol mode exercised.</param>
/// <param name="Outcome">Whether the provider satisfied the mode, failed it, or could not be asked.</param>
/// <param name="Detail">What was observed — recorded whatever the outcome.</param>
/// <param name="ElapsedMs">Wall-clock duration, useful when comparing providers.</param>
internal sealed record LlmProbeResult(LlmProbeMode Mode, LlmProbeOutcome Outcome, string Detail, long ElapsedMs);

/// <summary>
/// Runs the protocol modes against a live provider.
/// </summary>
/// <remarks>
/// <para>
/// Roughly 400 unit tests cover the LLM providers and every one of them speaks to a mocked
/// HTTP handler. A mock proves the framework sends what we believe it sends; it cannot prove
/// the vendor accepts it. This runner produces that second proof.
/// </para>
/// <para>
/// It never sees an API key as a value it could archive: the key is read from the environment
/// by the caller and lives only in the <see cref="LlmConfig"/> passed in. Nothing this class
/// returns contains credentials.
/// </para>
/// </remarks>
internal sealed class LlmProbeRunner
{
    private const string JsonPrompt = "Reply with a json object containing a single key \"ok\" set to true.";

    /// <summary>
    /// The value the tool hands back in M5. Deliberately unguessable: if it appears in the
    /// final answer, the round-trip really happened.
    /// </summary>
    private const string ToolProbeCode = "ORKEON-4711";

    private const string ToolProbePrompt =
        "What is the sealed probe code for the city of Lyon? Use the tool — the code cannot be guessed.";

    /// <summary>The number painted on <see cref="ProbeImagePng"/>. Not guessable at 1 in 100.</summary>
    private const string VisionProbeNumber = "73";

    /// <summary>
    /// A 160×112 PNG, 339 bytes: white "73" on a pure-red field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be a plain red square, and that made M9 undecidable. Asked for the dominant
    /// colour, <c>glm-4.6v-flash</c> answered "orange" (campaign 2026-08-02): nothing in the
    /// exchange could separate "saw red, named it badly" from "saw nothing, guessed a colour" —
    /// the answer space of a flat colour is small enough that a blind guess lands often.
    /// </para>
    /// <para>
    /// The number restores the discrimination the colour never had, and keeping the field red
    /// keeps the old signal as a second, independent channel. The two together say which half
    /// broke: colour right + number wrong is a model that cannot read, colour wrong too is an
    /// image that likely never arrived. One assertion could only ever have said "no".
    /// </para>
    /// </remarks>
    private const string ProbeImagePng =
        "iVBORw0KGgoAAAANSUhEUgAAAKAAAABwCAIAAAAWk+xVAAABGklEQVR42u3awRHAIAhFQfpv2hSRwe" +
        "TDOq8Ah/Umdao0OCMALMACLMACLMACDFiABViABVgXgBNPx7wC7w8YMGDAgAEDBgwYMGDAgAEDBgwY" +
        "MGDAMcB/q2NAXz2a5vsABgwYMGDAgAEDBgwYMGDAgAEDBgwYMOCAQYfPATBgwIABAwYMGDBgwIABAw" +
        "YMGDBgwIDXAW/AW734DhgwYMCAAQMGDBgwYMCAAQMGDBgwYMDNwD4Jhn82AAaMDbAAAwYMGDBgwIAB" +
        "AwYMWIABAx53f8CAAQMGDBgwYMCAAQMGDBgwYMCAAQMGbKm9+1h8BwwYMGDAgAEDBgwYMGDAgAEDBg" +
        "wYMGCFZASABViABViABViAAQuwAAuwAOtVD/3hw5DkBmQpAAAAAElFTkSuQmCC";

    private static readonly ToolSchema ToolProbeSchema = new(
        "orkeon_probe_lookup",
        "Looks up the sealed probe code for a city. The code is not public and cannot be inferred.",
        new Dictionary<string, ParameterSchema>
        {
            ["city"] = new("string", "The city to look the probe code up for.", Required: true, Example: "Lyon"),
        });

    private readonly ILlmProvider _provider;
    private readonly IToolCallParser? _nativeToolCallParser;
    private readonly TextFallbackToolCallParser _textToolCallParser;

    /// <summary>Initializes a new runner over an already-configured provider.</summary>
    /// <param name="provider">The provider under test.</param>
    /// <param name="nativeToolCallParser">
    /// The parser matching the provider's wire dialect, used by M5. Supplied by the caller
    /// because the dialect is a property of the provider it built, not of this runner. When
    /// omitted, M5 reports itself as not exercised rather than guessing a dialect.
    /// </param>
    public LlmProbeRunner(ILlmProvider provider, IToolCallParser? nativeToolCallParser = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _nativeToolCallParser = nativeToolCallParser;
        // The text protocol has no dialect: one parser serves every provider.
        _textToolCallParser = new TextFallbackToolCallParser(
            NullLogger<TextFallbackToolCallParser>.Instance);
    }

    /// <summary>Modes this harness can exercise without extra assets or a paid long-context call.</summary>
    /// <remarks>
    /// M11 (long context) and M14 (end-to-end crew) are deliberately absent: the first bills a
    /// request close to the model's advertised window, the second needs a real crew with
    /// delegation. Both are documented as manual procedures in the campaign kit.
    /// </remarks>
    /// <summary>
    /// The effort hint M7 sends. "low" is the cheapest and the default, but the value set is
    /// model-territory: mistral-medium-2604 refuses it outright ("supported values:
    /// ['high', 'none']", 2026-08-30). Supplied per model by the campaign catalogue's
    /// requiredParams registry, like the pinned temperature.
    /// </summary>
    public string M7ThinkingEffort { get; init; } = "low";

    public static IReadOnlyList<LlmProbeMode> SupportedModes { get; } =
    [
        LlmProbeMode.M1, LlmProbeMode.M2, LlmProbeMode.M3, LlmProbeMode.M4,
        LlmProbeMode.M5, LlmProbeMode.M6, LlmProbeMode.M7, LlmProbeMode.M8,
        LlmProbeMode.M9, LlmProbeMode.M10, LlmProbeMode.M12, LlmProbeMode.M13,
    ];

    /// <summary>Runs the requested modes in order and returns one result per mode.</summary>
    /// <param name="config">The effective configuration (model, key, base URL).</param>
    /// <param name="modes">The modes to exercise.</param>
    /// <param name="cancellationToken">Cancels the whole campaign.</param>
    /// <returns>One result per requested mode, in the order requested.</returns>
    public async Task<IReadOnlyList<LlmProbeResult>> RunAsync(
        LlmConfig config, IEnumerable<LlmProbeMode> modes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(modes);

        var results = new List<LlmProbeResult>();
        foreach (var mode in modes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunOneAsync(mode, config, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<LlmProbeResult> RunOneAsync(
        LlmProbeMode mode, LlmConfig config, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (outcome, detail) = await DispatchAsync(mode, config, cancellationToken).ConfigureAwait(false);
            return new LlmProbeResult(mode, outcome, detail, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A probe reports every failure as a failed mode; one bad mode must not abort the campaign.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // An exception is itself a finding: the framework's contract is to return typed
            // error responses, not to throw.
            return new LlmProbeResult(
                mode, LlmProbeOutcome.Failed, $"threw {ex.GetType().Name}: {ex.Message}",
                stopwatch.ElapsedMilliseconds);
        }
    }

    private Task<(LlmProbeOutcome Outcome, string Detail)> DispatchAsync(
        LlmProbeMode mode, LlmConfig config, CancellationToken cancellationToken) => mode switch
        {
            LlmProbeMode.M1 => ProbeSinglePromptAsync(config, cancellationToken),
            LlmProbeMode.M2 => ProbeMultiTurnAsync(config, cancellationToken),
            LlmProbeMode.M3 => ProbeTextStreamingAsync(config, cancellationToken),
            LlmProbeMode.M4 => ProbeChatStreamingAsync(config, cancellationToken),
            LlmProbeMode.M5 => ProbeNativeToolCallAsync(config, cancellationToken),
            LlmProbeMode.M6 => ProbeTextToolCallAsync(config, cancellationToken),
            LlmProbeMode.M7 => ProbeThinkingAsync(config, cancellationToken),
            LlmProbeMode.M8 => ProbeResponseFormatAsync(config, cancellationToken),
            LlmProbeMode.M9 => ProbeVisionAsync(config, cancellationToken),
            LlmProbeMode.M10 => ProbeContextCacheAsync(config, cancellationToken),
            LlmProbeMode.M12 => ProbeErrorHandlingAsync(config, cancellationToken),
            LlmProbeMode.M13 => ProbeCancellationAsync(config, cancellationToken),
            _ => Task.FromResult((LlmProbeOutcome.Failed, $"mode {mode} is not implemented by this harness")),
        };

    private async Task<(LlmProbeOutcome, string)> ProbeSinglePromptAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        var response = await _provider.GenerateAsync("Say hello in one short sentence.", config, cancellationToken)
            .ConfigureAwait(false);

        return Describe(response, r => !string.IsNullOrWhiteSpace(r.Content));
    }

    /// <summary>
    /// M2 — a system message must be honoured on <em>both</em> shapes the framework can send a
    /// conversation in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The OpenAI-compatible providers have two chat paths, and which one is taken depends on
    /// something the caller never thinks about: declare a tool and the conversation goes out as
    /// a real <c>messages</c> array; declare none and it is flattened into a single user turn
    /// reading <c>"user: …\nassistant: …"</c>. Exercising only one of them left a real defect
    /// invisible — D-02, where the configured system message was dropped on the native path
    /// alone. It took reading the code to find it, which is precisely what a probe should
    /// spare us.
    /// </para>
    /// <para>
    /// Running both also settles a question one shape cannot answer. When a provider fails the
    /// flattened shape and passes the structured one, the pseudo-transcript is what confused it
    /// — a framework problem. When it fails both, the model simply does not follow the
    /// instruction. That distinction decides whether anyone has anything to fix, and it is not
    /// reachable from a single call.
    /// </para>
    /// </remarks>
    private async Task<(LlmProbeOutcome, string)> ProbeMultiTurnAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        // The system message asks for a marker so the answer proves the turn was honoured
        // rather than merely produced.
        var withSystem = config with { SystemMessage = "Always end your reply with the token ORKEON_OK." };
        LlmMessage[] messages =
        [
            LlmMessage.User("What is 2 + 2?"),
            LlmMessage.Assistant("4"),
            LlmMessage.User("And multiplied by 3?"),
        ];

        var flattened = await RunMultiTurnShapeAsync(messages, withSystem, cancellationToken)
            .ConfigureAwait(false);
        if (flattened.Error is { } flatError)
            return (LlmProbeOutcome.Failed, $"conversation shape: {flatError}");

        // Declaring a tool is what selects the structured path — the same trigger a real agent
        // hits the moment it is given one. `None` emits tool_choice: "none": the schema still
        // travels (it has to, it is the trigger) but the model is forbidden from answering with
        // a call. Under `Auto` a model that reached for the tool would return no prose at all,
        // and the probe would read that as an ignored instruction — measuring the wrong thing.
        var structuredConfig = withSystem with { Tools = [ToolProbeSchema], ToolMode = ToolCallMode.None };
        var structured = await RunMultiTurnShapeAsync(messages, structuredConfig, cancellationToken)
            .ConfigureAwait(false);

        if (structured.Error is { } structError)
        {
            // A model with no tool support cannot be asked this question at all: the schema is
            // the only trigger for the structured path, so the request is refused before the
            // system message is ever evaluated. Reporting that as an M2 failure says "the system
            // message was lost" about an exchange in which it was never tested — `llava`, on
            // 2026-08-02, scored red on exactly this and sent the reader hunting for a defect
            // that was not there. Verdict on the shape that did run, and say the other is
            // unreachable rather than failed.
            if (IsToolCapabilityRefusal(structError))
            {
                // Not `Verdict(...)`: the local helper below shadows the class-level one.
                return (flattened.Honoured ? LlmProbeOutcome.Passed : LlmProbeOutcome.Failed,
                    flattened.Honoured
                    ? "system message honoured on the conversation shape; messages-array shape not "
                      + "exercisable — this model has no tool support, and a tool schema is the only "
                      + "trigger for that path"
                    : "system message ignored on the conversation shape, and the messages-array shape "
                      + "is not exercisable on a model without tool support");
            }

            return (LlmProbeOutcome.Failed, $"messages-array shape: {structError}");
        }

        var detail =
            $"conversation shape: {Verdict(flattened.Honoured)}, " +
            $"messages-array shape: {Verdict(structured.Honoured)}";

        return (flattened.Honoured, structured.Honoured) switch
        {
            (true, true) => (LlmProbeOutcome.Passed, $"system message honoured on both shapes ({detail})"),

            // The framework is at fault here, not the model: the same instruction lands when the
            // conversation keeps its structure, so flattening it is what lost the model.
            (false, true) => (LlmProbeOutcome.Failed,
                $"system message honoured only when the conversation keeps its structure — the flattened "
                + $"pseudo-transcript is what the model failed to follow ({detail})"),

            // Deliberately hedged. The structured path cannot be reached without putting a tool
            // schema in the request, so this outcome has two readings that the probe cannot
            // separate: a defect in the payload, or a model whose instruction-following degrades
            // once a tool catalogue shares its context. Naming only the first would send readers
            // hunting through code for something that may not be there.
            (true, false) => (LlmProbeOutcome.Failed,
                $"system message honoured on the flattened shape but lost on the messages array — inspect "
                + $"the structured payload, and bear in mind that reaching it requires sending a tool "
                + $"schema, which alone can cost a model some instruction-following ({detail})"),

            _ => (LlmProbeOutcome.Failed,
                $"system message ignored on both shapes — the instruction reaches the API either way, so "
                + $"this is the model not following it ({detail})"),
        };

        static string Verdict(bool honoured) => honoured ? "honoured" : "ignored";
    }

    /// <summary>
    /// Whether a vendor error is the API refusing tools for this model, rather than anything
    /// about the request Orkeon built.
    /// </summary>
    /// <remarks>
    /// Matched on the vendor's wording, the same way <c>CapabilityMismatchHint</c> does in the
    /// Infrastructure layer — that type is <c>internal</c> and this harness sits outside it, so
    /// the phrasings are restated rather than shared. Both lists are short and both are grounded
    /// in refusals actually observed, so the duplication is cheap; a shared public helper would
    /// widen Infrastructure's surface for one caller.
    /// </remarks>
    private static bool IsToolCapabilityRefusal(string vendorError) =>
        vendorError.Contains("does not support tools", StringComparison.OrdinalIgnoreCase)
        || vendorError.Contains("does not support function calling", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs one multi-turn call and says whether the marker came back.</summary>
    private async Task<(bool Honoured, string? Error)> RunMultiTurnShapeAsync(
        LlmMessage[] messages, LlmConfig config, CancellationToken cancellationToken)
    {
        var response = await _provider.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);

        return HasError(response)
            ? (false, ErrorOf(response))
            : (response.Content.Contains("ORKEON_OK", StringComparison.Ordinal), null);
    }

    private async Task<(LlmProbeOutcome, string)> ProbeTextStreamingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider is not IStreamingLlmProvider streaming)
            return (LlmProbeOutcome.NotApplicable, "provider does not implement IStreamingLlmProvider");

        var chunks = 0;
        var text = new StringBuilder();
        try
        {
            await foreach (var token in streaming
                .GenerateStreamingAsync(StreamingProbePrompt, config, cancellationToken)
                .ConfigureAwait(false))
            {
                chunks++;
                text.Append(token);
            }
        }
        catch (HttpRequestException ex)
        {
            // The token stream carries no metadata, so both a vendor refusal and a transport
            // failure arrive as the same exception type — and they are not the same finding.
            // `StatusCode` separates them: it is set only when a response came back. Calling a
            // connection failure "refused" blames the provider for something it never saw, which
            // is the D-04 mistake in another costume. Measured on Kimi at 11:41 on 2026-08-03,
            // where `An error occurred while sending the request.` was archived as a refusal.
            if (ex.StatusCode is null)
            {
                var cause = ex.InnerException is { } inner ? $"{inner.GetType().Name}: {inner.Message}" : ex.Message;
                return (LlmProbeOutcome.Failed, $"stream never reached the API: {cause}");
            }

            return (LlmProbeOutcome.Failed, $"stream refused: {ex.Message}");
        }

        return (Verdict(chunks > 1), $"{chunks} chunk(s), {text.Length} char(s)");
    }

    private async Task<(LlmProbeOutcome, string)> ProbeChatStreamingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider is not IStreamingLlmProvider streaming)
            return (LlmProbeOutcome.NotApplicable, "provider does not implement IStreamingLlmProvider");

        var deltas = 0;
        LlmResponse? final = null;
        await foreach (var ev in streaming.ChatStreamingAsync(
            [LlmMessage.User(StreamingProbePrompt)], config, cancellationToken).ConfigureAwait(false))
        {
            if (ev.Kind == LlmStreamEventKind.ContentDelta)
                deltas++;
            if (ev.Kind == LlmStreamEventKind.Completed)
                final = ev.FinalResponse;
        }

        // The Completed event's response is the same shape ChatAsync returns, errors included.
        // Reading only the counters made a rejected request read as "the stream produced nothing":
        // on Kimi (2026-08-03) M4 archived `0 content delta(s), completed=True, tokens=0` while
        // the refusal — a pinned temperature the model does not accept — sat unread in the very
        // response the probe was holding.
        if (final is not null && HasError(final))
            return (LlmProbeOutcome.Failed, $"stream refused: {ErrorOf(final)}");

        var completed = final is not null && !string.IsNullOrWhiteSpace(final.Content);
        var detail = $"{deltas} content delta(s), completed={final is not null}, tokens={final?.TokensUsed}";

        if (!completed)
            return (LlmProbeOutcome.Failed, detail);

        // A single delta carrying the whole answer is a buffered fallback wearing a stream's
        // clothes. M3 has always demanded more than one chunk; M4 accepting one made it pass
        // for providers that never streamed — Ollama is exactly that case, and nine untested
        // providers would have gone green the same way without anyone learning anything.
        return deltas > 1
            ? (LlmProbeOutcome.Passed, detail)
            : (LlmProbeOutcome.Failed,
               $"{detail} — the whole answer arrived in one event, which is a buffered fallback, not a stream");
    }

    /// <summary>
    /// M5 — the provider must emit a native tool call, and must then accept the tool's result
    /// back as a conversation turn. Half of that is easy; the round-trip is where dialects break.
    /// </summary>
    private async Task<(LlmProbeOutcome, string)> ProbeNativeToolCallAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_nativeToolCallParser is null)
            return (LlmProbeOutcome.NotApplicable, "no native tool-call parser was supplied to the harness");

        var withTools = config with { Tools = [ToolProbeSchema], ToolMode = ToolCallMode.Auto };
        var first = await _provider
            .ChatAsync([LlmMessage.User(ToolProbePrompt)], withTools, cancellationToken).ConfigureAwait(false);

        if (HasError(first))
            return (LlmProbeOutcome.Failed, ErrorOf(first));
        if (string.IsNullOrEmpty(first.RawResponseBody))
            return (LlmProbeOutcome.Failed, "no raw body returned: the native tool-calling path was not taken");

        using var body = JsonDocument.Parse(first.RawResponseBody);
        var calls = _nativeToolCallParser.ParseToolCalls(body.RootElement);
        if (calls.Count == 0)
            return (LlmProbeOutcome.Failed, $"no tool call in the reply: {Truncate(first.Content)}");

        var call = calls[0];
        if (!string.Equals(call.ToolName, ToolProbeSchema.Name, StringComparison.Ordinal))
            return (LlmProbeOutcome.Failed, $"called '{call.ToolName}' instead of '{ToolProbeSchema.Name}'");

        var replayToolCalls = RawOpenAiToolCalls(body.RootElement) ?? CanonicalToolCalls(calls);
        return await CompleteToolRoundTripAsync(withTools, replayToolCalls, call, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(LlmProbeOutcome, string)> CompleteToolRoundTripAsync(
        LlmConfig withTools, string replayToolCalls, ParsedToolCall call,
        CancellationToken cancellationToken)
    {
        // The real agent loop (NativeToolCallingAgentLoop) replays the vendor's tool_calls
        // fragment VERBATIM — GetRawText(), nothing rebuilt — and a probe that rebuilds from
        // the parsed calls measures a different product than the one shipping. Gemini turned
        // that difference into a 400 on 2026-08-30: its compat surface puts a
        // thought_signature inside each tool_call (extra_content.google) and rejects a replay
        // that lost it, so M5 failed for a defect the framework does not have. The raw
        // fragment is therefore replayed whenever the body carries one; the canonical rebuild
        // remains for dialects whose body is not OpenAI-shaped (Anthropic tool_use blocks),
        // because the OpenAI array is what AnthropicLlmProvider reads back.
        LlmMessage[] conversation =
        [
            LlmMessage.User(ToolProbePrompt),
            LlmMessage.Assistant("") with { RawToolCalls = replayToolCalls },
            new LlmMessage
            {
                Role = LlmRoles.Tool,
                Name = call.ToolName,
                ToolCallId = call.Id,
                Content = ToolProbeCode,
            },
        ];

        var second = await _provider.ChatAsync(conversation, withTools, cancellationToken).ConfigureAwait(false);
        if (HasError(second))
            return (LlmProbeOutcome.Failed, $"tool call parsed, but the result turn failed: {ErrorOf(second)}");

        var used = second.Content.Contains(ToolProbeCode, StringComparison.OrdinalIgnoreCase);
        return (Verdict(used), used
            ? $"{call.ToolName}({FormatArguments(call.Arguments)}) called, result accepted"
            : $"{call.ToolName} called, but the result turn ignored the value: {Truncate(second.Content)}");
    }

    /// <summary>
    /// M6 — the text protocol, for models with no native tool calling. The wording mirrors
    /// <c>AgentPromptComposer</c>'s "How to Call Tools" block, so a green M6 means the real
    /// agent loop would work here too.
    /// </summary>
    private async Task<(LlmProbeOutcome, string)> ProbeTextToolCallAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        var response = await _provider
            .GenerateAsync(TextToolProtocolPrompt(), config, cancellationToken).ConfigureAwait(false);

        if (HasError(response))
            return (LlmProbeOutcome.Failed, ErrorOf(response));

        var calls = _textToolCallParser.ParseToolCalls(AsTextEnvelope(response.Content));
        if (calls.Count == 0)
            return (LlmProbeOutcome.Failed, $"no parseable [TOOL_CALL] block: {Truncate(response.Content)}");

        var call = calls[0];
        var named = string.Equals(call.ToolName, ToolProbeSchema.Name, StringComparison.Ordinal);
        return (Verdict(named), named
            ? $"{call.ToolName}({FormatArguments(call.Arguments)}) parsed from text"
            : $"parsed a block naming '{call.ToolName}' instead of '{ToolProbeSchema.Name}'");
    }

    private async Task<(LlmProbeOutcome, string)> ProbeThinkingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider.Capabilities.Thinking == ThinkingSupport.None)
            return (LlmProbeOutcome.NotApplicable, "the provider declares no thinking capability");

        var withThinking = config with { Thinking = new LlmThinkingConfig { Effort = M7ThinkingEffort } };
        var response = await _provider.GenerateAsync(
            "Briefly, why is the sky blue?", withThinking, cancellationToken).ConfigureAwait(false);

        var traced = response.Metadata.ContainsKey("reasoning_content");
        return Describe(
            response,
            r => !string.IsNullOrWhiteSpace(r.Content),
            traced ? "reasoning trace returned" : "accepted, no reasoning trace returned");
    }

    private async Task<(LlmProbeOutcome, string)> ProbeResponseFormatAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (_provider.Capabilities.ResponseFormat == ResponseFormatSupport.None)
            return (LlmProbeOutcome.NotApplicable, "the provider declares no response-format capability");

        // Some APIs (Anthropic) have no schema-less JSON mode at all, so asking for a bare
        // json_object there constrains nothing and the mode would fail for the wrong reason.
        var format = _provider.Capabilities.ResponseFormat >= ResponseFormatSupport.JsonSchema
            ? LlmResponseFormat.JsonSchema(
                "probe_ok",
                """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""")
            : LlmResponseFormat.JsonObject();

        var constrained = config with { ResponseFormat = format };
        var response = await _provider.GenerateAsync(JsonPrompt, constrained, cancellationToken).ConfigureAwait(false);

        if (HasError(response))
            return (LlmProbeOutcome.Failed, ErrorOf(response));

        var isJson = IsParseableJson(response.Content);
        return (Verdict(isJson), isJson ? "valid JSON returned" : $"not JSON: {Truncate(response.Content)}");
    }

    /// <summary>
    /// M9 — vision. The matrix warns that vision is declared per provider while it is really a
    /// property of the model, so a red here on a text-only model is expected and informative:
    /// it is exactly the mismatch the matrix asks M9 to measure.
    /// </summary>
    /// <remarks>
    /// The image carries two independent facts — a number and a background colour — and the
    /// verdict names which one the model got. Reading the number is what passes: the colour is
    /// only there to tell a model that cannot read from an image that never arrived, a
    /// distinction the previous single-colour probe could not make.
    /// </remarks>
    private async Task<(LlmProbeOutcome, string)> ProbeVisionAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        if (!_provider.Capabilities.Vision)
            return (LlmProbeOutcome.NotApplicable, "the provider declares no vision capability");

        var content = MultiModalContent
            .FromText(
                "This image shows a number painted on a solid background. "
                + "Reply with exactly two words: the number, then the background colour in English.")
            .AddImage(ImageContentPart.FromBase64(ProbeImagePng, "image/png"));

        var response = await _provider
            .ChatAsync([LlmMessage.User(content)], config, cancellationToken).ConfigureAwait(false);

        if (HasError(response))
            return (LlmProbeOutcome.Failed, ErrorOf(response));

        var answer = response.Content ?? string.Empty;
        var readNumber = answer.Contains(VisionProbeNumber, StringComparison.Ordinal);
        var readColour = answer.Contains("red", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("rouge", StringComparison.OrdinalIgnoreCase);

        if (readNumber)
        {
            return (LlmProbeOutcome.Passed, readColour
                ? $"image read: number {VisionProbeNumber} and red background both named"
                : $"image read: number {VisionProbeNumber} named (background not named)");
        }

        // Colour without number: the bytes made it through and were decoded — the model simply
        // cannot resolve the glyphs. Worth separating, because it is a model limit rather than
        // the transport defect a bare red would have been read as.
        return (LlmProbeOutcome.Failed, readColour
            ? "image transited (red background named) but the number "
              + $"{VisionProbeNumber} was not read: {Truncate(answer)}"
            : $"image neither read nor described, expected {VisionProbeNumber} on red: {Truncate(answer)}");
    }

    /// <summary>
    /// M10 — prompt caching. Two calls sharing one long, stable prefix; the second must be
    /// billed as a cache hit.
    /// </summary>
    /// <remarks>
    /// The prefix is padded well past the ~1 K-token minimum most vendors impose before they
    /// cache anything at all — a short prefix would produce a red that says nothing about the
    /// framework. On Anthropic nothing is cached without an explicit breakpoint (G-17), so the
    /// probe opts in there and only there.
    /// </remarks>
    private async Task<(LlmProbeOutcome, string)> ProbeContextCacheAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        var cached = config with { SystemMessage = StableCachePrefix() };
        if (_provider.Capabilities.ExplicitPromptCaching)
            cached = cached with { Cache = LlmCacheConfig.SystemPrompt() };

        var first = await _provider
            .ChatAsync([LlmMessage.User("Reply with the single word: one.")], cached, cancellationToken)
            .ConfigureAwait(false);
        if (HasError(first))
            return (LlmProbeOutcome.Failed, ErrorOf(first));

        var second = await _provider
            .ChatAsync([LlmMessage.User("Reply with the single word: two.")], cached, cancellationToken)
            .ConfigureAwait(false);
        if (HasError(second))
            return (LlmProbeOutcome.Failed, ErrorOf(second));

        // No breakdown is an absence of vendor data, not a defect. But "no breakdown" alone does
        // not say which absence: a vendor that reports nothing, or a prefix that never got
        // cached. Kimi (2026-08-03) is exactly that ambiguity — Moonshot documents caching as
        // automatic and always on, yet the exchange carried no breakdown. Reporting the prompt
        // token counts lets the next reader tell the two apart without re-running blind: equal
        // counts with no breakdown point at a silent vendor, a second call that shrank points at
        // a cache Orkeon is failing to read.
        if (second.CacheHitTokens is not { } hit)
        {
            static string Count(int? tokens) => tokens?.ToString(CultureInfo.InvariantCulture) ?? "unreported";

            return (LlmProbeOutcome.NotApplicable,
                "the provider reports no cache-token breakdown "
                + $"(prompt tokens: {Count(first.PromptTokens)} then {Count(second.PromptTokens)})");
        }

        var ratio = second.CacheHitRatio is { } r ? $", ratio={r:F2}" : "";
        return (Verdict(hit > 0), $"second call: {hit} cached token(s){ratio}");
    }

    private async Task<(LlmProbeOutcome, string)> ProbeErrorHandlingAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        // A model that cannot exist: the contract is a typed error response, never a throw.
        var broken = config with { Model = "orkeon-nonexistent-model-probe" };
        var response = await _provider.GenerateAsync("hello", broken, cancellationToken).ConfigureAwait(false);

        var reported = HasError(response);
        return (Verdict(reported), reported
            ? $"typed error: {Truncate(ErrorOf(response))}"
            : "no error reported for an invalid model");
    }

    private async Task<(LlmProbeOutcome, string)> ProbeCancellationAsync(
        LlmConfig config, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Short enough that the request is still in flight, long enough that it has started.
        linked.CancelAfter(TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _provider.GenerateAsync(
                "Write a long essay about the history of cartography.", config, linked.Token).ConfigureAwait(false);

            // Returning a typed error is an acceptable outcome; returning full content is not.
            return HasError(response)
                ? (LlmProbeOutcome.Passed, $"cancelled into a typed error after {stopwatch.ElapsedMilliseconds} ms")
                : (LlmProbeOutcome.Failed, $"completed despite cancellation after {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (LlmProbeOutcome.Passed, $"cancelled after {stopwatch.ElapsedMilliseconds} ms");
        }
    }

    // ── Shared helpers ──────────────────────────────────────────────────────

    private static LlmProbeOutcome Verdict(bool passed) =>
        passed ? LlmProbeOutcome.Passed : LlmProbeOutcome.Failed;

    private static (LlmProbeOutcome, string) Describe(
        LlmResponse response, Func<LlmResponse, bool> predicate, string? note = null)
    {
        if (HasError(response))
            return (LlmProbeOutcome.Failed, ErrorOf(response));

        var detail = note ?? $"{response.Content.Length} char(s)";
        return (Verdict(predicate(response)), $"{detail}, tokens={response.TokensUsed}");
    }

    private static bool HasError(LlmResponse response) => response.Metadata.ContainsKey("error");

    private static string ErrorOf(LlmResponse response) =>
        response.Metadata.TryGetValue("error", out var error) ? error?.ToString() ?? "" : "";

    private static bool IsParseableJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using var _ = JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int max = 160) =>
        value.Length <= max ? value : value[..max] + "…";

    private static string FormatArguments(Dictionary<string, object?> arguments) =>
        string.Join(", ", arguments.Select(a => $"{a.Key}={a.Value}"));

    /// <summary>
    /// What M3 and M4 ask for. A hundred numbers, not five: a coarse-chunking stream fits a
    /// short answer in a single event and both modes then read a genuine stream as a buffered
    /// fallback. Calibrated twice on 2026-08-30 — Gemini emits ~13-character events (five
    /// numbers = one chunk, twenty = four), then claude-sonnet-5 emitted the entire
    /// eighty-character count-to-thirty as ONE delta while its chat path split the same
    /// answer in two. A hundred numbers is ~290 characters: several events on every vendor
    /// measured. The property pinned here is the count the prompt asks for, not its phrasing.
    /// </summary>
    private const string StreamingProbePrompt = "Count from one to one hundred, separated by spaces.";

    /// <summary>
    /// The vendor's own <c>tool_calls</c> fragment, verbatim, when the body is OpenAI-shaped —
    /// signatures and vendor extras included. Null for any other dialect.
    /// </summary>
    private static string? RawOpenAiToolCalls(JsonElement root) =>
        root.TryGetProperty("choices", out var choices)
        && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
        && choices[0].TryGetProperty("message", out var message)
        && message.TryGetProperty("tool_calls", out var toolCalls)
        && toolCalls.ValueKind == JsonValueKind.Array
            ? toolCalls.GetRawText()
            : null;

    /// <summary>Rebuilds the canonical <c>tool_calls</c> array Orkeon replays to every dialect.</summary>
    private static string CanonicalToolCalls(IReadOnlyList<ParsedToolCall> calls) =>
        JsonSerializer.Serialize(calls.Select(c => new
        {
            id = c.Id,
            type = "function",
            function = new { name = c.ToolName, arguments = JsonSerializer.Serialize(c.Arguments) },
        }));

    /// <summary>
    /// Wraps plain assistant text in the minimal body shape the fallback parser reads. The
    /// parser's entry point takes a response body because that is what the agent loop holds;
    /// a probe holds only the text, so it supplies the envelope.
    /// </summary>
    private static JsonElement AsTextEnvelope(string text) =>
        JsonSerializer.SerializeToElement(new
        {
            choices = new[] { new { message = new { content = text } } },
        });

    /// <summary>
    /// The instruction block, verbatim from <c>AgentPromptComposer.AppendTextToolCallInstructions</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It stays a plain literal — the braces are the protocol's own syntax, and interpolating
    /// around them is how a copy silently drifts from the format the parser accepts.
    /// </para>
    /// <para>
    /// It had drifted anyway: the first version dropped two rules and, more consequentially, the
    /// worked example. M6 was therefore measuring a prompt no agent ever sends, and a red verdict
    /// said nothing about production. The campaign of 2026-08-01 showed why the example earns its
    /// place — llama3.2 answered
    /// <c>[TOOL_CALL]{orkeon_probe_lookup: "orkeon_probe_lookup", args => {…}}</c>, folding the
    /// <c>name: description</c> shape of the tool listing into the block, which is exactly the
    /// confusion a filled-in example forecloses.
    /// </para>
    /// </remarks>
    private const string TextToolProtocolBlock = """

        ## How to Call Tools

        When you need to use a tool, you MUST emit a tool call block using this exact format:

        [TOOL_CALL]{tool => "tool_name", args => {--param1 "value1" --param2 "value2"}}[/TOOL_CALL]

        Rules:
        - Always use the exact tool name from the list above.
        - Each parameter is prefixed with -- followed by a space and the value in double quotes.
        - You can call ONE tool per [TOOL_CALL] block. To call multiple tools, emit multiple blocks.
        - After emitting a [TOOL_CALL] block, STOP and wait for the tool result before continuing.
        - Do NOT describe what you would do — actually call the tool.

        Example:
        [TOOL_CALL]{tool => "directory_read", args => {--path "/src"}}[/TOOL_CALL]

        """;

    private static string TextToolProtocolPrompt() =>
        $"You have one tool available.{Environment.NewLine}{Environment.NewLine}"
        + $"- {ToolProbeSchema.Name}: {ToolProbeSchema.Description}{Environment.NewLine}"
        + $"  Required: city{Environment.NewLine}"
        + TextToolProtocolBlock
        + Environment.NewLine
        + ToolProbePrompt;

    /// <summary>
    /// Builds a long, byte-for-byte stable prefix. Stability is the whole point: a timestamp or
    /// a GUID anywhere in here would rebuild the prefix on every call and the cache would never
    /// hit — the very failure mode <see cref="LlmResponse.CacheHitRatio"/> exists to surface.
    /// </summary>
    /// <remarks>
    /// Length is the other point, and it is vendor-territory: the 32-repetition version
    /// (~2050 tokens) was calibrated on "roughly a thousand tokens", and DashScope caches
    /// nothing at that size — three runs archived 0 cached tokens while the same call shape
    /// with a 5809-token prefix returned <c>cached_tokens: 4352</c> (2026-08-30). 128
    /// repetitions (~8200 tokens) clear the highest measured threshold with margin; the
    /// vendors that cached the short prefix (OpenAI, DeepSeek, Z.AI, Anthropic, x.AI) cache
    /// the long one identically.
    /// </remarks>
    private static string StableCachePrefix()
    {
        const string paragraph =
            "You are a cache-probe assistant. This paragraph exists only to give the request a " +
            "long, unchanging prefix, because most vendors refuse to cache anything shorter than " +
            "roughly a thousand tokens. It carries no instruction beyond answering the user " +
            "exactly as asked, in as few words as possible, with no preamble and no explanation. ";

        var builder = new StringBuilder(paragraph.Length * 128);
        for (var i = 0; i < 128; i++)
            builder.Append(paragraph);

        return builder.ToString();
    }
}
