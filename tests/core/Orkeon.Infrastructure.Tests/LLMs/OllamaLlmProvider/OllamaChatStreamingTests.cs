using System.Net;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Ollama's native streamed chat path.
/// </summary>
/// <remarks>
/// <para>
/// Ollama used to inherit the buffered fallback of <c>HttpLlmProviderBase</c>: one content event
/// carrying the whole answer, then the terminal event. Nothing errored — the streaming contract
/// was formally honoured — so a crew streaming from Ollama simply waited for the full response
/// and received it in one piece. It was the last provider in the fleet without a native path.
/// </para>
/// <para>
/// The campaign of 2026-08-01 is what surfaced it, and only after M4 was tightened to demand
/// more than a single delta: with the old assertion the buffered fallback passed as a stream.
/// The count assertions below are the unit-test half of that same guard.
/// </para>
/// </remarks>
public class OllamaChatStreamingTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<OllamaLlmProvider> _logger = new();

    private OllamaLlmProvider CreateProvider(LlmConfig config, TestHttpMessageHandler handler)
    {
        _httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient(handler));
        return new OllamaLlmProvider(
            config,
            _httpClientFactory,
            new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance),
            _logger);
    }

    private static LlmConfig BaseConfig() =>
        LlmConfig.Create("llama3.2") with { BaseUrl = new Uri("http://localhost:11434") };

    private static LlmConfig WithTools(LlmConfig config) => config with
    {
        Tools = [new ToolSchema("get_weather", "Get the weather", [])],
    };

    /// <summary>Ollama frames its stream as newline-delimited JSON, one object per token.</summary>
    private static TestHttpMessageHandler Ndjson(params string[] frames) =>
        TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, string.Join('\n', frames));

    private static async Task<List<LlmStreamEvent>> CollectAsync(
        OllamaLlmProvider provider, params LlmMessage[] messages)
    {
        var token = TestContext.Current.CancellationToken;
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(messages, null, token).WithCancellation(token))
            events.Add(ev);
        return events;
    }

    private static List<string> Deltas(List<LlmStreamEvent> events) =>
        [.. events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta).Select(e => e.Delta!)];

    private static LlmResponse Final(List<LlmStreamEvent> events) =>
        Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;

    private static async Task<JsonElement> PayloadAsync(HttpRequestMessage request)
    {
        var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    // ── The defect itself ───────────────────────────────────────────────────

    /// <summary>
    /// The assertion the buffered fallback cannot satisfy: several deltas, not one event holding
    /// the whole answer.
    /// </summary>
    [Fact]
    public async Task ShouldEmitOneDeltaPerToken_RatherThanTheWholeAnswerAtOnce()
    {
        using var handler = Ndjson(
            """{"response":"Bon","done":false}""",
            """{"response":"jour","done":false}""",
            """{"response":" !","done":false}""",
            """{"response":"","done":true,"prompt_eval_count":7,"eval_count":3}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var events = await CollectAsync(provider, LlmMessage.User("salut"));

        Assert.Equal(["Bon", "jour", " !"], Deltas(events));
        Assert.Equal("Bonjour !", Final(events).Content);
    }

    /// <summary>A stream that never asked to stream is the bug in its simplest form.</summary>
    [Fact]
    public async Task ShouldAskTheServerToStream()
    {
        using var handler = Ndjson("""{"response":"hi","done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        await CollectAsync(provider, LlmMessage.User("hello"));

        Assert.True((await PayloadAsync(handler.CapturedRequests.Single())).GetProperty("stream").GetBoolean());
    }

    // ── Endpoint selection ──────────────────────────────────────────────────

    /// <summary>
    /// Asking for deltas must not move the conversation to another endpoint: that would change
    /// system-message handling and the tool dialect as a side effect of streaming.
    /// </summary>
    [Fact]
    public async Task ShouldStayOnGenerate_ForAPlainConversation()
    {
        using var handler = Ndjson("""{"response":"hi","done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        await CollectAsync(provider, LlmMessage.User("hello"));

        Assert.EndsWith("/api/generate", handler.CapturedRequests.Single().RequestUri!.AbsolutePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSwitchToChat_WhenToolsAreDeclared()
    {
        using var handler = Ndjson(
            """{"message":{"role":"assistant","content":"hi"},"done":false}""",
            """{"message":{"role":"assistant","content":""},"done":true}""");
        using var provider = CreateProvider(WithTools(BaseConfig()), handler);

        var events = await CollectAsync(provider, LlmMessage.User("hello"));

        Assert.EndsWith("/api/chat", handler.CapturedRequests.Single().RequestUri!.AbsolutePath,
            StringComparison.Ordinal);
        Assert.Equal(["hi"], Deltas(events));
    }

    /// <summary>The system message must survive the streamed structured path too.</summary>
    [Fact]
    public async Task ShouldCarryTheConfiguredSystemMessage_OnTheStreamedChatPath()
    {
        using var handler = Ndjson("""{"message":{"role":"assistant","content":"ok"},"done":true}""");
        var config = WithTools(BaseConfig()) with { SystemMessage = "Always answer in Latin." };
        using var provider = CreateProvider(config, handler);

        await CollectAsync(provider, LlmMessage.User("hello"));

        var messages = (await PayloadAsync(handler.CapturedRequests.Single())).GetProperty("messages");
        Assert.Equal("Always answer in Latin.", messages[0].GetProperty("content").GetString());
    }

    // ── The terminal event ──────────────────────────────────────────────────

    /// <summary>
    /// The counts ride on the terminal frame, which is why the loop reads it instead of breaking
    /// on <c>done</c> — the mistake <c>GenerateStreamingAsync</c> makes, losing usage entirely.
    /// </summary>
    [Fact]
    public async Task ShouldReportUsage_FromTheTerminalFrame()
    {
        using var handler = Ndjson(
            """{"response":"hi","done":false}""",
            """{"response":"","done":true,"prompt_eval_count":11,"eval_count":4}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var final = Final(await CollectAsync(provider, LlmMessage.User("hello")));

        Assert.Equal(11, final.PromptTokens);
        Assert.Equal(4, final.CompletionTokens);
        Assert.Equal(15, final.TokensUsed);
    }

    /// <summary>
    /// A streamed tool call has to reach the terminal response in the OpenAI shape, or the single
    /// tool-call parser the framework owns cannot read it and the call is lost.
    /// </summary>
    [Fact]
    public async Task ShouldSynthesizeTheOpenAiBody_ForAStreamedToolCall()
    {
        using var handler = Ndjson(
            """{"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"get_weather","arguments":{"city":"Lyon"}}}]},"done":false}""",
            """{"message":{"role":"assistant","content":""},"done":true}""");
        using var provider = CreateProvider(WithTools(BaseConfig()), handler);

        var final = Final(await CollectAsync(provider, LlmMessage.User("weather?")));

        Assert.NotNull(final.RawResponseBody);
        using var body = JsonDocument.Parse(final.RawResponseBody!);
        var call = body.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("tool_calls")[0].GetProperty("function");
        Assert.Equal("get_weather", call.GetProperty("name").GetString());
        // Arguments as a JSON *string*, the OpenAI shape — Ollama sends an object.
        Assert.Equal(JsonValueKind.String, call.GetProperty("arguments").ValueKind);
    }

    /// <summary>No tool call means no synthesized body, leaving the text fallback in charge.</summary>
    [Fact]
    public async Task ShouldLeaveTheRawBodyNull_WhenNoToolWasCalled()
    {
        using var handler = Ndjson("""{"message":{"role":"assistant","content":"plain"},"done":true}""");
        using var provider = CreateProvider(WithTools(BaseConfig()), handler);

        var final = Final(await CollectAsync(provider, LlmMessage.User("hi")));

        Assert.Null(final.RawResponseBody);
    }

    // ── Thinking ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldStreamThinkingAsReasoningDeltas()
    {
        using var handler = Ndjson(
            """{"message":{"role":"assistant","thinking":"let me ","content":""},"done":false}""",
            """{"message":{"role":"assistant","thinking":"see","content":""},"done":false}""",
            """{"message":{"role":"assistant","content":"42"},"done":true}""");
        using var provider = CreateProvider(WithTools(BaseConfig()), handler);

        var events = await CollectAsync(provider, LlmMessage.User("hi"));

        Assert.Equal(["let me ", "see"],
            events.Where(e => e.Kind == LlmStreamEventKind.ReasoningDelta).Select(e => e.Delta));
        Assert.Equal("let me see", Assert.Contains("reasoning_content", Final(events).Metadata));
    }

    // ── Resilience ──────────────────────────────────────────────────────────

    /// <summary>One bad frame must not take the stream down with it.</summary>
    [Fact]
    public async Task ShouldSkipAMalformedFrame_AndKeepStreaming()
    {
        using var handler = Ndjson(
            """{"response":"a","done":false}""",
            "{ this is not json",
            """{"response":"b","done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var events = await CollectAsync(provider, LlmMessage.User("hi"));

        Assert.Equal(["a", "b"], Deltas(events));
    }

    /// <summary>
    /// An HTTP failure ends in a typed terminal event, not an exception and not an empty stream:
    /// a caller awaiting the Completed event would otherwise hang on nothing.
    /// </summary>
    [Fact]
    public async Task ShouldEndWithATypedError_WhenTheServerRefuses()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.NotFound, """{"error":"model 'ghost' not found"}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var events = await CollectAsync(provider, LlmMessage.User("hi"));

        var only = Assert.Single(events);
        Assert.Equal(LlmStreamEventKind.Completed, only.Kind);
        // The error rides in the metadata and Content stays empty — same shape as the
        // non-streaming path, which is the point: a caller must not have to special-case it.
        Assert.Empty(only.FinalResponse!.Content);
        Assert.Contains("ghost", Assert.Contains("error", only.FinalResponse.Metadata)?.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>Exactly one terminal event, always — the contract every consumer relies on.</summary>
    [Fact]
    public async Task ShouldEmitExactlyOneTerminalEvent()
    {
        using var handler = Ndjson(
            """{"response":"a","done":false}""",
            """{"response":"b","done":true,"prompt_eval_count":1,"eval_count":2}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var events = await CollectAsync(provider, LlmMessage.User("hi"));

        Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed);
        Assert.Equal(LlmStreamEventKind.Completed, events[^1].Kind);
    }
}
