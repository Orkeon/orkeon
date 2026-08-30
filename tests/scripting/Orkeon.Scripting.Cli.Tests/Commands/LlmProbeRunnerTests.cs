using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The campaign harness behind <c>orkeon llm probe</c> (LLM-08/C1), exercised against a
/// scripted provider so the harness itself is covered without spending a credit.
/// </summary>
/// <remarks>
/// The campaigns this harness enables must run against real APIs — that is their entire
/// point. What can and must be verified offline is that the harness judges correctly: that a
/// failed mode is reported as failed, that an exception is a finding rather than a crash, and
/// that a provider lacking a capability is not marked down for it.
/// </remarks>
public sealed class LlmProbeRunnerTests
{
    private const string ProbeTool = "orkeon_probe_lookup";
    private const string ProbeCode = "ORKEON-4711";

    private static LlmConfig Config() => LlmConfig.Create("probe-model", "unused-in-tests");

    private static IToolCallParser OpenAiParser() =>
        new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance).Parser;

    private static async Task<LlmProbeResult> RunAsync(
        LlmProbeRunner runner, LlmProbeMode mode)
    {
        var results = await runner.RunAsync(Config(), [mode], TestContext.Current.CancellationToken);
        return Assert.Single(results);
    }

    // ── M1, M12: the baseline contract ──────────────────────────────────────

    [Fact]
    public async Task ShouldPassM1_WhenTheProviderReturnsContent()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "hello there" });

        var result = await RunAsync(runner, LlmProbeMode.M1);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task ShouldFailM1_WhenTheProviderReturnsAnErrorResponse()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Error = "401 Unauthorized" });

        var result = await RunAsync(runner, LlmProbeMode.M1);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("401", result.Detail, StringComparison.Ordinal);
    }

    // ── M2: the same instruction, both conversation shapes ──────────────────

    /// <summary>
    /// M2 sends the conversation twice — flattened and as a real messages array — because the
    /// framework picks between those two shapes on its own, and a defect can live in one alone.
    /// The four combinations say different things, so each gets its own verdict.
    /// </summary>
    [Fact]
    public async Task ShouldPassM2_WhenBothShapesHonourTheSystemMessage()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider().Script(
            new LlmResponse { Content = "12. ORKEON_OK" },
            new LlmResponse { Content = "12. ORKEON_OK" }));

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("both shapes", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Honoured when the conversation keeps its structure, ignored once flattened: the
    /// pseudo-transcript is what lost the model. That is a framework finding, and the detail
    /// has to say so — otherwise the reader blames the model and fixes nothing.
    /// </summary>
    [Fact]
    public async Task ShouldFailM2_AndBlameTheFlattening_WhenOnlyTheStructuredShapeIsHonoured()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider().Script(
            new LlmResponse { Content = "12." },
            new LlmResponse { Content = "12. ORKEON_OK" }));

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("flattened pseudo-transcript", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mirror image, and the shape D-02 had: the instruction survives flattening but is
    /// lost on the messages array the native path builds. The verdict stays hedged on purpose —
    /// reaching that path requires sending a tool schema, which can cost a model some
    /// instruction-following on its own, and the probe cannot tell the two apart.
    /// </summary>
    [Fact]
    public async Task ShouldFailM2_AndPointAtTheStructuredPayload_WhenOnlyTheFlattenedShapeIsHonoured()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider().Script(
            new LlmResponse { Content = "12. ORKEON_OK" },
            new LlmResponse { Content = "12." }));

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("inspect the structured payload", result.Detail, StringComparison.Ordinal);
        Assert.Contains("instruction-following", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ignored either way: the instruction reached the API on both shapes, so nothing in the
    /// framework explains it. Saying that plainly is what stops a fruitless hunt.
    /// </summary>
    [Fact]
    public async Task ShouldFailM2_AndBlameTheModel_WhenNeitherShapeIsHonoured()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider().Script(
            new LlmResponse { Content = "12." },
            new LlmResponse { Content = "12." }));

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("the model not following it", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>An API error is a failure of the call, not a verdict on the instruction.</summary>
    [Fact]
    public async Task ShouldFailM2_NamingTheShapeThatErrored()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Error = "429 Too Many Requests" });

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("conversation shape", result.Detail, StringComparison.Ordinal);
        Assert.Contains("429", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The `llava` case of 2026-08-02. A tool schema is the only trigger for the structured path,
    /// so a model without tool support has its request refused before the system message is ever
    /// looked at. Calling that an M2 failure asserts something the exchange never tested — and it
    /// reads as "the system message was lost", which sends the reader after a phantom defect.
    /// </summary>
    [Fact]
    public async Task ShouldPassM2_WhenTheModelHasNoTools_AndTheReachableShapeIsHonoured()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider().Script(
            new LlmResponse { Content = "12. ORKEON_OK" },
            ErrorResponse("registry.ollama.ai/library/llava:latest does not support tools")));

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("not exercisable", result.Detail, StringComparison.Ordinal);
        Assert.Contains("no tool support", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unreachable is not a free pass: the shape that did run still has to honour the instruction.
    /// </summary>
    [Fact]
    public async Task ShouldFailM2_WhenTheModelHasNoTools_AndIgnoresTheOnlyReachableShape()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider().Script(
            new LlmResponse { Content = "12." },
            ErrorResponse("registry.ollama.ai/library/llava:latest does not support tools")));

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("ignored on the conversation shape", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exemption is for capability refusals only. Any other error on the structured shape is
    /// still a failed call — widening it would let real breakage pass as "not exercisable".
    /// </summary>
    [Fact]
    public async Task ShouldFailM2_WhenTheStructuredShapeErrorsForAnyOtherReason()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider().Script(
            new LlmResponse { Content = "12. ORKEON_OK" },
            ErrorResponse("500 Internal Server Error")));

        var result = await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("messages-array shape", result.Detail, StringComparison.Ordinal);
        Assert.Contains("500", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second call must declare a tool — that is the only thing that selects the structured
    /// path. Without it M2 would send the same shape twice and prove nothing.
    /// </summary>
    [Fact]
    public async Task ShouldSelectTheStructuredPath_ByDeclaringAToolOnTheSecondCall()
    {
        var provider = new ScriptedProvider().Script(
            new LlmResponse { Content = "12. ORKEON_OK" },
            new LlmResponse { Content = "12. ORKEON_OK" });
        var runner = new LlmProbeRunner(provider);

        await RunAsync(runner, LlmProbeMode.M2);

        Assert.Equal(2, provider.Configs.Count);
        Assert.Null(provider.Configs[0]?.Tools);
        Assert.NotNull(provider.Configs[1]?.Tools);
        // tool_choice: "none" — the schema is only there to select the path, never to be called.
        Assert.Equal(ToolCallMode.None, provider.Configs[1]?.ToolMode);
        // Both must carry the instruction, or the comparison is meaningless.
        Assert.All(provider.Configs, c => Assert.Contains("ORKEON_OK", c?.SystemMessage, StringComparison.Ordinal));
    }

    /// <summary>
    /// The framework's contract is a typed error response, never a throw — so an exception is
    /// itself a finding, and must be recorded rather than aborting the campaign.
    /// </summary>
    [Fact]
    public async Task ShouldRecordAThrownException_AsAFailedModeRatherThanAbortingTheRun()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Throw = new InvalidOperationException("boom") });

        var results = await runner.RunAsync(
            Config(), [LlmProbeMode.M1, LlmProbeMode.M12], TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Equal(LlmProbeOutcome.Failed, results[0].Outcome);
        Assert.Contains("InvalidOperationException", results[0].Detail, StringComparison.Ordinal);
    }

    // ── M3 / M4: a refused stream must say it was refused ───────────────────

    /// <summary>
    /// The Kimi finding of 2026-08-03. Both streaming modes reported an empty stream for a
    /// request the API had rejected, and the rejection — a pinned temperature the model does not
    /// accept — appeared nowhere in the archived report. An empty stream and a refused request
    /// are different facts and must not read alike.
    /// </summary>
    [Fact]
    public async Task ShouldFailM3_NamingTheRefusal_WhenTheTokenStreamThrows()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            StreamThrow = new HttpRequestException(
                "Kimi API error: BadRequest - invalid temperature: only 1 is allowed for this model",
                inner: null,
                statusCode: HttpStatusCode.BadRequest),
        });

        var result = await RunAsync(runner, LlmProbeMode.M3);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("stream refused", result.Detail, StringComparison.Ordinal);
        Assert.Contains("invalid temperature", result.Detail, StringComparison.Ordinal);
        // The old wording is what made the report undiagnosable — it must be gone, not merely
        // supplemented.
        Assert.DoesNotContain("0 chunk(s)", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A connection that never reached the API is not a refusal.
    /// </summary>
    /// <remarks>
    /// Both arrive as <see cref="HttpRequestException"/>, and the first version of this probe
    /// called them both "stream refused" — so Kimi was recorded as having rejected a request it
    /// never received (2026-08-03, 11:41: `An error occurred while sending the request.`). Same
    /// mistake as D-04, different costume: blaming a named party for an unattributed failure.
    /// <see cref="HttpRequestException.StatusCode"/> is set only when a response came back.
    /// </remarks>
    [Fact]
    public async Task ShouldFailM3_WithoutBlamingTheProvider_WhenTheRequestNeverReachedIt()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            StreamThrow = new HttpRequestException(
                "An error occurred while sending the request.",
                new IOException("connection reset by peer")),
        });

        var result = await RunAsync(runner, LlmProbeMode.M3);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("never reached the API", result.Detail, StringComparison.Ordinal);
        // The useful half is the inner cause; the outer message says nothing.
        Assert.Contains("connection reset by peer", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// M4's refusal arrives inside the Completed event rather than as an exception, because that
    /// signature does have a channel for it. Reading only the delta counters threw the message away.
    /// </summary>
    [Fact]
    public async Task ShouldFailM4_NamingTheRefusal_WhenTheCompletedEventCarriesAnError()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Error = "Kimi API error: BadRequest - invalid temperature: only 1 is allowed for this model",
        });

        var result = await RunAsync(runner, LlmProbeMode.M4);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("stream refused", result.Detail, StringComparison.Ordinal);
        Assert.Contains("invalid temperature", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("content delta(s)", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>A stream that simply produced nothing still reports the counters, not a refusal.</summary>
    [Fact]
    public async Task ShouldFailM3_WithTheCounters_WhenTheStreamIsMerelyEmpty()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "" });

        var result = await RunAsync(runner, LlmProbeMode.M3);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("0 chunk(s)", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", result.Detail, StringComparison.Ordinal);
    }

    // ── M4: a stream must actually stream ───────────────────────────────────

    /// <summary>
    /// A single event carrying the whole answer is a buffered fallback wearing a stream's
    /// clothes. M4 used to accept it, so a provider that never streamed went green — Ollama did
    /// exactly that, and nine untested providers would have followed.
    /// </summary>
    [Fact]
    public async Task ShouldFailM4_WhenTheWholeAnswerArrivesInOneEvent()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "one-shot" });

        var result = await RunAsync(runner, LlmProbeMode.M4);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("buffered fallback, not a stream", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldPassM4_WhenTheAnswerArrivesInSeveralDeltas()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "one two three", StreamInChunks = true });

        var result = await RunAsync(runner, LlmProbeMode.M4);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
    }

    /// <summary>
    /// The demanded answer must be long enough to discriminate a coarse-chunking stream from a
    /// buffered fallback. "Count from one to five" was not: Gemini genuinely streams, but its
    /// compat surface emits ~13-character chunks, so five numbers fit in one event and both M3
    /// and M4 read a real stream as buffered (2026-08-30) — while the same endpoint produced
    /// four chunks the moment the count went to twenty. Thirty keeps a margin. The property
    /// pinned here is the count the prompt asks for, not its phrasing.
    /// </summary>
    [Fact]
    public async Task ShouldDemandAnAnswerLongEnoughToDiscriminate_OnBothStreamingModes()
    {
        var provider = new ScriptedProvider { Content = "one two three", StreamInChunks = true };
        var runner = new LlmProbeRunner(provider);

        await RunAsync(runner, LlmProbeMode.M3);
        await RunAsync(runner, LlmProbeMode.M4);

        Assert.Contains("thirty", Assert.Single(provider.Prompts), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thirty", Assert.Single(provider.Conversations)[0].Content, StringComparison.OrdinalIgnoreCase);
    }

    // ── Capability gates: absence is not failure ────────────────────────────

    /// <summary>A provider that declares no capability must not be marked down for lacking it.</summary>
    [Fact]
    public Task ShouldSkipTheThinkingMode_WhenTheProviderDeclaresItAbsent() =>
        AssertModeIsSkippedAsync(LlmProbeMode.M7);

    /// <inheritdoc cref="ShouldSkipTheThinkingMode_WhenTheProviderDeclaresItAbsent"/>
    [Fact]
    public Task ShouldSkipTheResponseFormatMode_WhenTheProviderDeclaresItAbsent() =>
        AssertModeIsSkippedAsync(LlmProbeMode.M8);

    /// <inheritdoc cref="ShouldSkipTheThinkingMode_WhenTheProviderDeclaresItAbsent"/>
    [Fact]
    public Task ShouldSkipTheVisionMode_WhenTheProviderDeclaresItAbsent() =>
        AssertModeIsSkippedAsync(LlmProbeMode.M9);

    private static async Task AssertModeIsSkippedAsync(LlmProbeMode mode)
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "irrelevant" });

        var result = await RunAsync(runner, mode);

        Assert.Equal(LlmProbeOutcome.NotApplicable, result.Outcome);
    }

    /// <summary>
    /// M5 needs the dialect's parser, which only the caller can pick. Without one the harness
    /// must say it did not look, not invent a verdict.
    /// </summary>
    [Fact]
    public async Task ShouldSkipM5_WhenNoNativeToolCallParserIsSupplied()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "irrelevant" });

        var result = await RunAsync(runner, LlmProbeMode.M5);

        Assert.Equal(LlmProbeOutcome.NotApplicable, result.Outcome);
        Assert.Contains("parser", result.Detail, StringComparison.Ordinal);
    }

    // ── M8: response format ─────────────────────────────────────────────────

    [Fact]
    public async Task ShouldFailM8_WhenTheConstrainedAnswerIsNotJson()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = "Sure! Here is your answer.",
            Capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonObject },
        });

        Assert.Equal(LlmProbeOutcome.Failed, (await RunAsync(runner, LlmProbeMode.M8)).Outcome);
    }

    [Fact]
    public async Task ShouldPassM8_WhenTheConstrainedAnswerParses()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = """{"ok":true}""",
            Capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonObject },
        });

        Assert.Equal(LlmProbeOutcome.Passed, (await RunAsync(runner, LlmProbeMode.M8)).Outcome);
    }

    // ── M5: native tool calling, including the round-trip ───────────────────

    [Fact]
    public async Task ShouldPassM5_WhenTheToolIsCalledAndItsResultIsUsed()
    {
        var provider = new ScriptedProvider().Script(
            ToolCallResponse(ProbeTool, """{"city":"Lyon"}"""),
            new LlmResponse { Content = $"The sealed probe code for Lyon is {ProbeCode}." });
        var runner = new LlmProbeRunner(provider, OpenAiParser());

        var result = await RunAsync(runner, LlmProbeMode.M5);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("city=Lyon", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Emitting the call is the easy half. The turn that feeds the result back is where a
    /// dialect mismatch shows up, so a provider that never uses the value must read red.
    /// </summary>
    [Fact]
    public async Task ShouldFailM5_WhenTheResultTurnIgnoresTheToolOutput()
    {
        var provider = new ScriptedProvider().Script(
            ToolCallResponse(ProbeTool, """{"city":"Lyon"}"""),
            new LlmResponse { Content = "I am not able to determine the code." });
        var runner = new LlmProbeRunner(provider, OpenAiParser());

        var result = await RunAsync(runner, LlmProbeMode.M5);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("ignored", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailM5_WhenTheProviderAnswersInProseInsteadOfCallingTheTool()
    {
        var provider = new ScriptedProvider().Script(new LlmResponse
        {
            Content = "I would look that up.",
            RawResponseBody = """{"choices":[{"message":{"content":"I would look that up."}}]}""",
        });
        var runner = new LlmProbeRunner(provider, OpenAiParser());

        var result = await RunAsync(runner, LlmProbeMode.M5);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("no tool call", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The round-trip must replay the assistant turn in Orkeon's canonical shape and answer it
    /// with a tool-role message — that is what both the OpenAI base and Anthropic read back.
    /// </summary>
    [Fact]
    public async Task ShouldReplayM5_AsACanonicalAssistantTurnFollowedByAToolResult()
    {
        var provider = new ScriptedProvider().Script(
            ToolCallResponse(ProbeTool, """{"city":"Lyon"}"""),
            new LlmResponse { Content = ProbeCode });
        var runner = new LlmProbeRunner(provider, OpenAiParser());

        await RunAsync(runner, LlmProbeMode.M5);

        var replay = provider.Conversations[1];
        Assert.Equal(3, replay.Length);
        Assert.Contains(ProbeTool, replay[1].RawToolCalls, StringComparison.Ordinal);
        Assert.Equal("tool", replay[2].Role);
        Assert.Equal("call-1", replay[2].ToolCallId);
        Assert.Equal(ProbeCode, replay[2].Content);
    }

    /// <summary>
    /// When the body already carries the OpenAI <c>tool_calls</c> array, the probe must
    /// replay THAT fragment — the raw one — not a canonical rebuild of it. The real agent
    /// loop (<c>NativeToolCallingAgentLoop</c>) replays <c>tc.GetRawText()</c> verbatim, and
    /// a probe that rebuilds from the parsed calls measures a different product than the one
    /// shipping. Gemini turned the difference into a 400: its compat surface returns a
    /// <c>thought_signature</c> inside each tool_call (<c>extra_content.google</c>) and
    /// rejects a replay that lost it — which is exactly what the rebuild did, failing M5 on
    /// 2026-08-30 for a defect the framework does not have.
    /// </summary>
    [Fact]
    public async Task ShouldReplayM5_WithTheRawToolCallsFragment_WhenTheBodyIsOpenAiShaped()
    {
        var provider = new ScriptedProvider().Script(
            GeminiShapedToolCallResponse(ProbeTool, """{"city":"Lyon"}"""),
            new LlmResponse { Content = ProbeCode });
        var runner = new LlmProbeRunner(provider, OpenAiParser());

        var result = await RunAsync(runner, LlmProbeMode.M5);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        var replay = provider.Conversations[1];
        Assert.Contains("thought_signature", replay[1].RawToolCalls, StringComparison.Ordinal);
        Assert.Contains("sig-opaque-blob", replay[1].RawToolCalls, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dialect whose body is not OpenAI-shaped (Anthropic's <c>tool_use</c> content blocks)
    /// has no raw fragment to lift, so the canonical rebuild remains the replay — it is what
    /// <c>AnthropicLlmProvider</c> reads back.
    /// </summary>
    [Fact]
    public async Task ShouldReplayM5_AsTheCanonicalRebuild_WhenTheBodyIsNotOpenAiShaped()
    {
        var provider = new ScriptedProvider().Script(
            AnthropicShapedToolCallResponse(ProbeTool, "toolu-1"),
            new LlmResponse { Content = ProbeCode });
        var runner = new LlmProbeRunner(
            provider, new AnthropicToolCallingStrategy().Parser);

        var result = await RunAsync(runner, LlmProbeMode.M5);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        var replay = provider.Conversations[1];
        Assert.Contains(ProbeTool, replay[1].RawToolCalls, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"function\"", replay[1].RawToolCalls, StringComparison.Ordinal);
        Assert.Equal("toolu-1", replay[2].ToolCallId);
    }

    // ── M6: the text fallback protocol ──────────────────────────────────────

    [Fact]
    public async Task ShouldPassM6_WhenTheModelEmitsAParseableToolCallBlock()
    {
        // Plain concatenation: the braces here are the protocol's own syntax, and every form of
        // interpolation escaping around them is a chance to drift from what the parser accepts.
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = "[TOOL_CALL]{tool => \"" + ProbeTool + "\", args => {--city \"Lyon\"}}[/TOOL_CALL]",
        });

        var result = await RunAsync(runner, LlmProbeMode.M6);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains(ProbeTool, result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailM6_WhenTheModelDescribesTheCallInsteadOfEmittingIt()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = "I would call orkeon_probe_lookup with the city Lyon.",
        });

        var result = await RunAsync(runner, LlmProbeMode.M6);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("no parseable", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 must send the prompt production sends, or its verdict is about nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The probe's copy of the instruction block had already drifted from
    /// <c>AgentPromptComposer.AppendTextToolCallInstructions</c>, dropping two rules and the
    /// worked example. The example is the load-bearing part: without it llama3.2 folded the
    /// <c>name: description</c> shape of the tool listing into the call block
    /// (campaign of 2026-08-01), a failure production had no reason to reproduce.
    /// </para>
    /// <para>
    /// This pins the probe side only — the composer's method is private, so a true equality check
    /// would mean widening its visibility for a test. An edit to production still has to be
    /// mirrored here by hand; what this catches is the copy losing lines on its own, which is the
    /// drift that actually happened.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ShouldSendTheProductionToolProtocol_IncludingTheWorkedExample()
    {
        var provider = new ScriptedProvider { Content = "no call" };

        await RunAsync(new LlmProbeRunner(provider), LlmProbeMode.M6);

        var prompt = string.Join('\n',
            provider.Prompts.Concat(provider.Conversations.SelectMany(c => c).Select(m => m.Content ?? "")));

        Assert.Contains("Example:", prompt, StringComparison.Ordinal);
        Assert.Contains(
            "[TOOL_CALL]{tool => \"directory_read\", args => {--path \"/src\"}}[/TOOL_CALL]",
            prompt, StringComparison.Ordinal);
        Assert.Contains("You can call ONE tool per [TOOL_CALL] block", prompt, StringComparison.Ordinal);
        Assert.Contains("STOP and wait for the tool result", prompt, StringComparison.Ordinal);
    }

    // ── M9: vision ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldPassM9_WhenTheModelReadsTheNumberAndTheColour()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = "73 red",
            Capabilities = new LlmProviderCapabilities { Vision = true },
        });

        var result = await RunAsync(runner, LlmProbeMode.M9);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("both named", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading the number is the assertion; the colour is corroboration. A model that answers
    /// the harder half and skips the easier one has still proved it saw the image.
    /// </summary>
    [Fact]
    public async Task ShouldPassM9_WhenTheModelReadsTheNumberWithoutNamingTheColour()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = "The number is 73.",
            Capabilities = new LlmProviderCapabilities { Vision = true },
        });

        var result = await RunAsync(runner, LlmProbeMode.M9);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("background not named", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case that forced this probe to be rewritten. The colour alone used to be the whole
    /// assertion, so this answer scored green while proving nothing: a blind guess over a flat
    /// colour lands often enough to be worthless as evidence. It now fails, and the detail says
    /// the image did arrive — a model limit, not the transport defect a bare red would suggest.
    /// </summary>
    [Fact]
    public async Task ShouldFailM9_WhenTheModelNamesTheColourButCannotReadTheNumber()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = "The background is red.",
            Capabilities = new LlmProviderCapabilities { Vision = true },
        });

        var result = await RunAsync(runner, LlmProbeMode.M9);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("image transited", result.Detail, StringComparison.Ordinal);
        Assert.Contains("73 was not read", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Vision is declared per provider while it is really a property of the model. A text-only
    /// model behind a vision-capable API must therefore read red, not skipped: that mismatch is
    /// exactly what the matrix asks M9 to measure.
    /// </summary>
    [Fact]
    public async Task ShouldFailM9_WhenTheModelCannotActuallySeeTheImage()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider
        {
            Content = "I am a text-only model and cannot view images.",
            Capabilities = new LlmProviderCapabilities { Vision = true },
        });

        var result = await RunAsync(runner, LlmProbeMode.M9);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("neither read nor described", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt has to ask for both facts, or the number half of the verdict measures the
    /// question rather than the model.
    /// </summary>
    [Fact]
    public async Task ShouldAskM9_ForBothTheNumberAndTheColour()
    {
        var provider = new ScriptedProvider
        {
            Content = "73 red",
            Capabilities = new LlmProviderCapabilities { Vision = true },
        };

        await RunAsync(new LlmProbeRunner(provider), LlmProbeMode.M9);

        var prompt = Assert.Single(Assert.Single(provider.Conversations)).MultiModalContent!.ToTextOnly();
        Assert.Contains("the number", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("colour", prompt, StringComparison.OrdinalIgnoreCase);
        // Naming the answer in the question would let a blind model score green.
        Assert.DoesNotContain("73", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("red", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The image is a base64 constant split across source lines for width. A reformat that
    /// mangles the split would still compile and still "send an image", and every M9 would
    /// then read red for a reason no report could explain — so the bytes are pinned here.
    /// </summary>
    [Fact]
    public async Task ShouldSendM9_AsAMultiModalMessageCarryingARealPng()
    {
        var provider = new ScriptedProvider
        {
            Content = "73 red",
            Capabilities = new LlmProviderCapabilities { Vision = true },
        };
        var runner = new LlmProbeRunner(provider);

        await RunAsync(runner, LlmProbeMode.M9);

        var sent = Assert.Single(provider.Conversations);
        var content = Assert.Single(sent).MultiModalContent;
        Assert.NotNull(content);
        Assert.True(content.HasText);

        var image = Assert.IsType<ImageContentPart>(Assert.Single(content.Parts.Skip(1)));
        Assert.Equal("image/png", image.MimeType);

        Assert.NotNull(image.Data);
        var bytes = image.Data.ToArray();
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], bytes[..8]);
        // IHDR carries the dimensions big-endian at offset 16; 160×112 is what was encoded.
        Assert.Equal(160u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(112u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    // ── M10: prompt caching ─────────────────────────────────────────────────

    [Fact]
    public async Task ShouldPassM10_WhenTheSecondCallReportsCachedTokens()
    {
        var provider = new ScriptedProvider().Script(
            new LlmResponse { Content = "one", CacheHitTokens = 0, CacheMissTokens = 2048 },
            new LlmResponse { Content = "two", CacheHitTokens = 2048, CacheMissTokens = 12 });
        var runner = new LlmProbeRunner(provider);

        var result = await RunAsync(runner, LlmProbeMode.M10);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("2048 cached token(s)", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailM10_WhenTheProviderReportsABreakdownWithNoHit()
    {
        var provider = new ScriptedProvider().Script(
            new LlmResponse { Content = "one", CacheHitTokens = 0, CacheMissTokens = 2048 },
            new LlmResponse { Content = "two", CacheHitTokens = 0, CacheMissTokens = 2048 });
        var runner = new LlmProbeRunner(provider);

        Assert.Equal(LlmProbeOutcome.Failed, (await RunAsync(runner, LlmProbeMode.M10)).Outcome);
    }

    /// <summary>
    /// A provider that publishes no cache figures has told us nothing — that is an absence of
    /// vendor data, not a defect anyone can fix.
    /// </summary>
    [Fact]
    public async Task ShouldSkipM10_WhenTheProviderPublishesNoCacheBreakdown()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "ok" });

        var result = await RunAsync(runner, LlmProbeMode.M10);

        Assert.Equal(LlmProbeOutcome.NotApplicable, result.Outcome);
    }

    /// <summary>
    /// On Anthropic nothing is cached at all unless a breakpoint is placed (G-17), so probing
    /// the cache there without opting in would measure the absence of the opt-in, not the API.
    /// </summary>
    [Fact]
    public async Task ShouldOptIntoCaching_OnlyWhenTheProviderRequiresAnExplicitBreakpoint()
    {
        var explicitProvider = new ScriptedProvider
        {
            Content = "ok",
            Capabilities = new LlmProviderCapabilities { ExplicitPromptCaching = true },
        };
        var implicitProvider = new ScriptedProvider { Content = "ok" };

        await RunAsync(new LlmProbeRunner(explicitProvider), LlmProbeMode.M10);
        await RunAsync(new LlmProbeRunner(implicitProvider), LlmProbeMode.M10);

        Assert.True(explicitProvider.Configs[0]?.Cache?.CacheSystemPrompt);
        Assert.Null(implicitProvider.Configs[0]?.Cache);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>An OpenAI-shaped body carrying one tool call — what the wire really looks like.</summary>
    private static LlmResponse ToolCallResponse(string tool, string argumentsJson) => new()
    {
        Content = "",
        RawResponseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        // OpenAI sends the arguments as a JSON *string*, not as an object.
                        tool_calls = new[]
                        {
                            new
                            {
                                id = "call-1",
                                type = "function",
                                function = new { name = tool, arguments = argumentsJson },
                            },
                        },
                    },
                },
            },
        }),
    };

    /// <summary>
    /// Gemini's compat surface, verbatim shape of 2026-08-30: each tool_call carries an
    /// <c>extra_content.google.thought_signature</c> the vendor demands back on replay.
    /// </summary>
    private static LlmResponse GeminiShapedToolCallResponse(string tool, string argumentsJson) => new()
    {
        Content = "",
        RawResponseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = "call-1",
                                type = "function",
                                function = new { name = tool, arguments = argumentsJson },
                                extra_content = new { google = new { thought_signature = "sig-opaque-blob" } },
                            },
                        },
                    },
                },
            },
        }),
    };

    /// <summary>Anthropic Messages API shape: tool_use content blocks, no tool_calls array.</summary>
    private static LlmResponse AnthropicShapedToolCallResponse(string tool, string id) => new()
    {
        Content = "",
        RawResponseBody = JsonSerializer.Serialize(new
        {
            content = new object[]
            {
                new { type = "tool_use", id, name = tool, input = new { city = "Lyon" } },
            },
        }),
    };

    /// <summary>A provider whose behaviour the test dictates, so the harness's judgement is what is measured.</summary>
    /// <summary>
    /// A scripted response the runner reads as an API error. <see cref="ScriptedProvider.Error"/>
    /// fails every call; this fails one, which is what the per-shape cases need.
    /// </summary>
    private static LlmResponse ErrorResponse(string error) => new()
    {
        Content = "",
        Metadata = LlmResponseMetadata.CreateBuilder()
            .AddProvider("scripted")
            .AddError(error)
            .Build()
            .ToDictionary(),
    };

    private sealed class ScriptedProvider : ILlmProvider, IStreamingLlmProvider
    {
        private readonly Queue<LlmResponse> _scripted = new();

        public string Content { get; init; } = "";
        public string? Error { get; init; }
        public Exception? Throw { get; init; }

        public string Name => "scripted";

        public bool SupportsStreaming => true;

        public LlmProviderCapabilities Capabilities { get; init; } = LlmProviderCapabilities.Unknown;

        /// <summary>Every message array handed to <see cref="ChatAsync"/>, in call order.</summary>
        public List<LlmMessage[]> Conversations { get; } = [];

        /// <summary>Every configuration handed to the provider, in call order.</summary>
        public List<LlmConfig?> Configs { get; } = [];

        /// <summary>
        /// Every prompt handed to <see cref="GenerateAsync"/>, in call order. Modes that use the
        /// prompt-completion path leave <see cref="Conversations"/> empty, so what they sent is
        /// only observable here.
        /// </summary>
        public List<string> Prompts { get; } = [];

        /// <summary>Queues responses to return in order; the default response serves once they run out.</summary>
        public ScriptedProvider Script(params LlmResponse[] responses)
        {
            foreach (var response in responses)
                _scripted.Enqueue(response);
            return this;
        }

        public Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            Configs.Add(config);
            Prompts.Add(prompt);

            if (Throw is not null)
                throw Throw;

            if (_scripted.Count > 0)
                return Task.FromResult(_scripted.Dequeue());

            var metadata = LlmResponseMetadata.CreateBuilder().AddProvider(Name);
            if (Error is not null)
                metadata.AddError(Error);

            return Task.FromResult(new LlmResponse
            {
                Content = Error is null ? Content : "",
                TokensUsed = 5,
                Metadata = metadata.Build().ToDictionary(),
            });
        }

        public Task<LlmResponse> ChatAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            Conversations.Add(messages);
            return GenerateAsync("", config, cancellationToken);
        }

        /// <summary>
        /// Thrown from the token stream instead of yielding. Models a refused request: that
        /// signature has no metadata to carry an error, so the providers throw
        /// <see cref="HttpRequestException"/> rather than end an empty sequence normally.
        /// </summary>
        public Exception? StreamThrow { get; init; }

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Prompts.Add(prompt);
            await Task.CompletedTask.ConfigureAwait(false);

            if (StreamThrow is not null)
                throw StreamThrow;

            foreach (var chunk in Content.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                yield return chunk;
        }

        /// <summary>
        /// When false, the whole answer arrives in one event — the buffered fallback several
        /// providers really serve, which M4 must tell apart from a genuine stream.
        /// </summary>
        public bool StreamInChunks { get; init; }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Conversations.Add(messages);
            await Task.CompletedTask.ConfigureAwait(false);

            // A refused chat stream yields no delta and a Completed event whose response carries
            // the error — exactly what OpenAICompatibleProviderBase does on a non-2xx.
            if (Error is not null)
            {
                yield return LlmStreamEvent.Complete(new LlmResponse
                {
                    Content = "",
                    Metadata = LlmResponseMetadata.CreateBuilder()
                        .AddProvider(Name).AddError(Error).Build().ToDictionary(),
                });
                yield break;
            }

            if (StreamInChunks)
            {
                foreach (var word in Content.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    yield return LlmStreamEvent.Content(word);
            }
            else
            {
                yield return LlmStreamEvent.Content(Content);
            }

            yield return LlmStreamEvent.Complete(new LlmResponse { Content = Content });
        }
    }
}
