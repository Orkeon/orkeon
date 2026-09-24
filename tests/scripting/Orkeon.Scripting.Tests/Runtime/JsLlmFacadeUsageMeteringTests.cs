using System.Runtime.CompilerServices;
using Jint;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Runtime;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The scripted surface on the token meter (STUDIO-42): the host hands <c>ctx.llm</c> a
/// metered provider, the provider reports each call — once, whatever path it takes — and
/// the facade says whose call it is: the <c>ctx.llm.*</c> method as the operation, the
/// context's crew and agent. Before the meter moved into the provider the facade reported
/// its responses itself; what used to be tested here (estimates, vendor charges, a failing
/// sink) now lives with <see cref="MeteredLlmProvider"/>.
/// </summary>
public sealed class JsLlmFacadeUsageMeteringTests
{
    private static Jint.Native.JsValue Evaluate(Engine engine, string js) => engine.Evaluate(js);

    private static string ToolCallBody(string name)
        => "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
           "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":\"{}\"}}]}}]}";

    private static JsLlmFacade Facade(
        Engine engine, ILlmProvider provider, ILlmUsageSink sink, IBaseTool[]? tools = null, ILlmDeltaSink? deltas = null)
        => new(engine, MeteredLlmProvider.Wrap(provider, sink), CancellationToken.None, tools ?? Array.Empty<IBaseTool>(),
               observability: new JsLlmObservability
               {
                   DeltaSink = deltas,
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
    public async Task A_complete_call_is_metered_once_for_the_contexts_crew_and_agent()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var provider = new ScriptedProvider(WithUsage("ok", 100, 20));
        var facade = Facade(engine, provider, sink);

        await facade.complete("hello", null);

        var e = Assert.Single(sink.Events);
        Assert.Equal("complete", e.OperationType);
        Assert.Equal("main-loop", e.CrewId);
        Assert.Equal("assistant", e.AgentId);
        Assert.Equal("scripted", e.Provider);
        Assert.Equal("fake-model", e.Model);
        Assert.Equal(100, e.PromptTokens);
        Assert.Equal(20, e.CompletionTokens);
        Assert.Equal(provider.Calls, sink.Events.Count);
    }

    [Fact]
    public async Task Chat_extract_and_decide_are_each_metered_under_their_own_name()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var provider = new ScriptedProvider(WithUsage("chatted", 10, 1), WithUsage("{\"a\":1}", 10, 2), WithUsage("yes", 10, 3));
        var facade = Facade(engine, provider, sink);

        await facade.chat(Evaluate(engine, "([{ role: \"user\", content: \"hi\" }])"), null);
        await facade.extract("shape this", Jint.Native.JsValue.Undefined, null);
        await facade.decide("pick", Evaluate(engine, "([\"yes\", \"no\"])"), null);

        string[] expected = ["chat", "extract", "decide"];
        Assert.Equal(expected, sink.Events.Select(e => e.OperationType));
        Assert.Equal(provider.Calls, sink.Events.Count);
    }

    [Fact]
    public async Task Act_is_metered_once_per_iteration()
    {
        // Two iterations — a tool call, then the final answer — are two calls: two events,
        // no more.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var provider = new ScriptedProvider(WithUsage("", 50, 5, raw: ToolCallBody("probe_tool")), WithUsage("final", 60, 6));
        var facade = Facade(engine, provider, sink, [new EchoTool("probe_tool")]);

        var result = await facade.ActAsync(engine, "go", null);

        Assert.Equal("final", result.Get("output").AsString());
        Assert.Equal(2, provider.Calls);
        Assert.Equal(provider.Calls, sink.Events.Count);
        Assert.All(sink.Events, e => Assert.Equal("act", e.OperationType));
        Assert.Equal(50 + 60, sink.Events.Sum(e => e.PromptTokens));
    }

    [Fact]
    public async Task A_streamed_act_turn_is_metered_once_with_the_usage_of_its_final_response()
    {
        // With a delta sink the act loop streams: the metered stream reports its final
        // response, and nothing else reports the turn a second time.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var provider = new OneTurnStreamingProvider(WithUsage("streamed final", 200, 30));
        var facade = Facade(engine, provider, sink, deltas: new NullDeltaSink());

        var result = await facade.ActAsync(engine, "go", null);

        Assert.Equal("streamed final", result.Get("output").AsString());
        var e = Assert.Single(sink.Events);
        Assert.Equal("act", e.OperationType);
        Assert.Equal("assistant", e.AgentId);
        Assert.Equal(200, e.PromptTokens);
        Assert.Equal(30, e.CompletionTokens);
    }

    [Fact]
    public async Task A_stream_is_metered_once_under_the_stream_name_on_either_path()
    {
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var streaming = Facade(engine, new OneTurnStreamingProvider(WithUsage("streamed", 5, 7)), sink);
        var buffered = Facade(engine, new ScriptedProvider(WithUsage("buffered", 5, 7)), sink);

        await foreach (var _ in streaming.StreamChunks("p", null)) { }
        await foreach (var _ in buffered.StreamChunks("p", null)) { }

        Assert.Equal(2, sink.Events.Count);
        Assert.All(sink.Events, e =>
        {
            Assert.Equal("stream", e.OperationType);
            Assert.Equal("assistant", e.AgentId);
            Assert.Equal(7, e.CompletionTokens);
        });
    }

    [Fact]
    public async Task Extract_is_metered_even_when_its_answer_does_not_parse()
    {
        // The tokens were paid before the parse failed: the meter counts the call, not the
        // outcome.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(WithUsage("this is not JSON", 80, 8)), sink);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => facade.extract("shape this", Jint.Native.JsValue.Undefined, null));

        var e = Assert.Single(sink.Events);
        Assert.Equal("extract", e.OperationType);
        Assert.Equal(80, e.PromptTokens);
    }

    [Fact]
    public async Task A_script_running_inside_an_agents_task_keeps_that_task()
    {
        // The facade names the operation, the crew and the agent it knows; the task comes from
        // the scope the host opened around the script.
        using var engine = new Engine();
        var sink = new RecordingUsageSink();
        var facade = Facade(engine, new ScriptedProvider(WithUsage("ok", 1, 1)), sink);

        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, taskId: "task-9"))
            await facade.complete("hello", null);

        var e = Assert.Single(sink.Events);
        Assert.Equal("complete", e.OperationType);
        Assert.Equal("task-9", e.TaskId);
    }

    // ── doubles ──────────────────────────────────────────────────────────────

    private sealed class RecordingUsageSink : ILlmUsageSink
    {
        public List<CostUsageEvent> Events { get; } = new();
        public void Record(CostUsageEvent usage) => Events.Add(usage);
    }

    private sealed class NullDeltaSink : ILlmDeltaSink
    {
        public void OnDelta(string delta) { }
        public void OnTurnCompleted() { }
    }

    /// <summary>Replays scripted responses on every surface (Generate and Chat share the queue) and counts the calls.</summary>
    private sealed class ScriptedProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _i;

        public ScriptedProvider(params LlmResponse[] responses) => _responses = responses;

        public int Calls => _i;

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
