using System.Runtime.CompilerServices;
using Jint;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The <see cref="ILlmUsageSink"/> emission contract: every <c>ctx.llm.*</c> path reports
/// each completed LLM call exactly once, with the crew/agent/provider coordinates a host
/// needs to account for it. Before the sink existed NOTHING observed scripted usage — the
/// REPL's session token counter read a cost report that was never fed.
/// </summary>
public sealed class JsLlmFacadeUsageSinkTests
{
    private static Jint.Native.JsValue EvalOptions(Engine engine, string js) => engine.Evaluate(js);

    private static string ToolCallBody(string name)
        => "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
           "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":\"{}\"}}]}}]}";

    private static JsLlmFacade Facade(Engine engine, ILlmProvider provider, ILlmUsageSink sink, IBaseTool[]? tools = null)
        => new(engine, provider, CancellationToken.None, tools ?? Array.Empty<IBaseTool>(),
               observability: new JsLlmObservability
               {
                   UsageSink = sink,
                   CrewName = "main-loop",
                   AgentName = "assistant",
               });

    private static LlmResponse WithUsage(string content, int prompt, int completion, string? raw = null)
        => new()
        {
            Content = content,
            RawResponseBody = raw,
            TokensUsed = prompt + completion,
            PromptTokens = prompt,
            CompletionTokens = completion,
            Model = "fake-model",
        };

    [Fact]
    public async Task Complete_reports_one_event_with_the_full_coordinates()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(WithUsage("ok", 100, 20)), sink);

        await facade.complete("hello", null);

        var e = Assert.Single(sink.Events);
        Assert.Equal("complete", e.OperationType);
        Assert.Equal("main-loop", e.CrewId);
        Assert.Equal("assistant", e.AgentId);
        Assert.Equal("scripted", e.Provider);
        Assert.Equal("fake-model", e.Model);
        Assert.Equal(100, e.PromptTokens);
        Assert.Equal(20, e.CompletionTokens);
    }

    [Fact]
    public async Task Chat_and_extract_and_decide_each_report_their_path()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(
            WithUsage("chatted", 10, 1),
            WithUsage("{\"a\":1}", 10, 2),
            WithUsage("yes", 10, 3)), sink);

        var messages = EvalOptions(engine, "([{ role: \"user\", content: \"hi\" }])");
        var choices = EvalOptions(engine, "([\"yes\", \"no\"])");
        await facade.chat(messages, null);
        await facade.extract("shape this", Jint.Native.JsValue.Undefined, null);
        await facade.decide("pick", choices, null);

        string[] expected = ["chat", "extract", "decide"];
        Assert.Equal(expected, sink.Events.Select(e => e.OperationType));
    }

    [Fact]
    public async Task Act_reports_exactly_one_event_per_iteration()
    {
        // Two iterations (tool call, then final answer) ⇒ two events, no more: the
        // anti-double-count pin. SendChatAsync assembles the response; only the act
        // loop reports it.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(
            WithUsage("", 50, 5, raw: ToolCallBody("probe_tool")),
            WithUsage("final", 60, 6)), sink, new IBaseTool[] { new EchoTool("probe_tool") });

        var result = await facade.act("go", null);

        Assert.Equal("final", result.Get("output").AsString());
        Assert.Equal(2, sink.Events.Count);
        Assert.All(sink.Events, e => Assert.Equal("act", e.OperationType));
        Assert.Equal(50 + 60, sink.Events.Sum(e => e.PromptTokens));
    }

    [Fact]
    public async Task Streamed_act_still_reports_once_per_iteration()
    {
        // With a delta sink registered the act loop takes ChatViaStreamAsync; the
        // usage of the Completed event's final response must count exactly once.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var provider = new OneTurnStreamingProvider(WithUsage("streamed final", 200, 30));
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, Array.Empty<IBaseTool>(),
            observability: new JsLlmObservability
            {
                DeltaSink = new NullDeltaSink(),
                UsageSink = sink,
                CrewName = "main-loop",
                AgentName = "assistant",
            });

        var result = await facade.act("go", null);

        Assert.Equal("streamed final", result.Get("output").AsString());
        var e = Assert.Single(sink.Events);
        Assert.Equal("act", e.OperationType);
        Assert.Equal(200, e.PromptTokens);
        Assert.Equal(30, e.CompletionTokens);
    }

    [Fact]
    public async Task Stream_fallback_reports_through_the_stream_path()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(WithUsage("chunk", 5, 7)), sink);

        await foreach (var _ in facade.StreamChunks("p", null)) { }

        var e = Assert.Single(sink.Events);
        Assert.Equal("stream", e.OperationType);
        Assert.Equal(7, e.CompletionTokens);
    }

    [Fact]
    public async Task Extract_reports_the_paid_tokens_even_when_the_parse_throws()
    {
        // The emission sits BEFORE the JSON parse (design-review pin): a provider
        // that answers prose makes extract throw, but the tokens were paid and must
        // be counted regardless.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(WithUsage("this is not JSON", 80, 8)), sink);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => facade.extract("shape this", Jint.Native.JsValue.Undefined, null));

        var e = Assert.Single(sink.Events);
        Assert.Equal("extract", e.OperationType);
        Assert.Equal(80, e.PromptTokens);
    }

    /// <summary>
    /// A response with no usage at all used to be dropped on the floor — no event, no meter
    /// movement, and a client reading «0 tokens» through a whole session with a provider
    /// that simply does not count. Silence is not evidence that nothing was spent, so the
    /// runtime counts it itself and SAYS that the figure is its own.
    /// <para>
    /// «No usage» still never reads as «zero tokens»: it reads as an approximation, which
    /// is what it is, and <c>Estimated</c> is how every consumer downstream can tell.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_response_with_no_usage_at_all_is_estimated_and_marked_as_such()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(new LlmResponse { Content = "une réponse" }), sink);

        await facade.complete("dis-moi quelque chose d'assez long pour compter", null);

        var e = Assert.Single(sink.Events);
        Assert.True(e.Estimated);
        Assert.True(e.PromptTokens > 0);
        Assert.True(e.CompletionTokens > 0);
    }

    [Fact]
    public async Task A_provider_that_counts_is_taken_at_its_word_never_estimated_over()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(WithUsage("ok", 100, 20)), sink);

        await facade.complete("hello", null);

        var e = Assert.Single(sink.Events);
        Assert.False(e.Estimated);
        Assert.Equal(100, e.PromptTokens);
        Assert.Equal(20, e.CompletionTokens);
    }

    /// <summary>
    /// The chat paths estimate from the CONVERSATION, message overhead included — a chat
    /// costs more than the concatenation of its texts, and the estimate must not pretend
    /// otherwise.
    /// </summary>
    [Fact]
    public async Task A_silent_chat_is_estimated_from_the_whole_conversation()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(new LlmResponse { Content = "ok" }), sink);
        var messages = EvalOptions(engine, "[{role:'system',content:'" + new string('a', 350)
            + "'},{role:'user',content:'" + new string('b', 350) + "'}]");

        await facade.chat(messages, null);

        var e = Assert.Single(sink.Events);
        Assert.True(e.Estimated);
        // 700 characters at ~3.5 per token, plus the per-message overhead: comfortably
        // above what either message would score alone.
        Assert.True(e.PromptTokens >= 200, $"prompt estimate was {e.PromptTokens}");
    }

    [Fact]
    public async Task A_total_only_response_lands_entirely_on_completion()
    {
        // Providers that report only a grand total leave the split null: the mapping
        // must keep PromptTokens + CompletionTokens == TokensUsed instead of inventing
        // a split.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(
            new LlmResponse { Content = "ok", TokensUsed = 42, Model = "fake-model" }), sink);

        await facade.complete("hello", null);

        var e = Assert.Single(sink.Events);
        Assert.Equal(0, e.PromptTokens);
        Assert.Equal(42, e.CompletionTokens);
    }

    [Fact]
    public async Task A_throwing_sink_never_fails_the_llm_call()
    {
        using var engine = new Engine();
        var facade = Facade(engine, new ScriptedProvider(WithUsage("ok", 1, 1)), new ThrowingUsageSink());

        Assert.Equal("ok", await facade.complete("hello", null));
    }

    // ── doubles ──────────────────────────────────────────────────────────────

    private sealed class RecordingUsageSink : ILlmUsageSink
    {
        public List<CostUsageEvent> Events { get; } = new();
        public void Record(CostUsageEvent usage) => Events.Add(usage);
    }

    private sealed class ThrowingUsageSink : ILlmUsageSink
    {
        public void Record(CostUsageEvent usage) => throw new InvalidOperationException("sink down");
    }

    private sealed class NullDeltaSink : ILlmDeltaSink
    {
        public void OnDelta(string delta) { }
        public void OnTurnCompleted() { }
    }

    /// <summary>Replays scripted responses on every surface (Generate and Chat share the queue).</summary>
    private sealed class ScriptedProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _i;

        public ScriptedProvider(params LlmResponse[] responses) => _responses = responses;

        public string Name => "scripted";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_responses[Math.Min(_i++, _responses.Length - 1)]);
    }

    private sealed class OneTurnStreamingProvider : ILlmProvider, IStreamingLlmProvider
    {
        private readonly LlmResponse _final;

        public OneTurnStreamingProvider(LlmResponse final) => _final = final;

        public string Name => "streaming";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };
        public bool SupportsStreaming => true;

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_final);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(_final);

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return _final.Content;
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return LlmStreamEvent.Content(_final.Content);
            yield return LlmStreamEvent.Complete(_final);
            await Task.CompletedTask;
        }
    }

    private sealed class EchoTool : IBaseTool
    {
        public EchoTool(string name) => Name = name;

        public string Name { get; }
        public ToolAccess Access => ToolAccess.Read;
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
