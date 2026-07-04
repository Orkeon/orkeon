using Jint;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using System.Runtime.CompilerServices;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// exp07 F5 L1: <c>ctx.llm.stream</c> streams per-chunk when the provider implements
/// <see cref="IStreamingLlmProvider"/>, and the <c>onDelta</c> act option receives each
/// content delta before the loop resumes with the assembled final response — composing
/// with the budget (F1) and permission gate (F2) unchanged.
/// </summary>
public sealed class JsLlmFacadeStreamTests
{
    private static readonly string[] HelloChunks = ["He", "llo"];
    private static readonly StreamedTurn[] NoTurns = [];
    private static readonly string[] NoChunks = [];
    private static readonly string[] FullTextChunk = ["full text"];

    [Fact]
    public async Task Stream_yields_provider_chunks_when_streaming_supported()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: HelloChunks,
            turns: NoTurns);

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var chunks = new List<string>();
        await foreach (var c in facade.stream("hi", null))
            chunks.Add(c);

        Assert.Equal(HelloChunks, chunks);
    }

    [Fact]
    public async Task Stream_falls_back_to_single_chunk_for_non_streaming_provider()
    {
        using var engine = new Engine();
        var provider = new PlainProvider("full text");

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var chunks = new List<string>();
        await foreach (var c in facade.stream("hi", null))
            chunks.Add(c);

        Assert.Equal(FullTextChunk, chunks);
    }

    [Fact]
    public async Task Act_onDelta_receives_each_content_delta_and_final_output_is_assembled()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns:
            [
                new StreamedTurn(Deltas: ["par", "tial", " answer"], Final: new LlmResponse { Content = "partial answer" }),
            ]);

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        engine.SetValue("__deltas", new List<object>());
        var options = BuildOptions(engine, "({ onDelta: d => __deltas.push(d) })");
        var result = await facade.act("go", options);

        Assert.Equal("partial answer", result.Get("output").AsString());
        var deltas = (List<object>)engine.GetValue("__deltas").ToObject()!;
        Assert.Equal(new object[] { "par", "tial", " answer" }, deltas.ToArray());
        Assert.Equal(1, provider.ChatStreamingCalls);
        Assert.Equal(0, provider.ChatCalls); // streamed path replaced the buffered one
    }

    [Fact]
    public async Task Act_onDelta_composes_with_tool_calls_budget_and_gate()
    {
        using var engine = new Engine();
        var tool = new RecordingTool("file_read");
        var toolCallBody =
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
            "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"file_read\",\"arguments\":\"{}\"}}]}}]}";
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns:
            [
                // Turn 1: streams nothing visible, ends with a tool call (synthesized body).
                new StreamedTurn(Deltas: [], Final: new LlmResponse { Content = "", RawResponseBody = toolCallBody, TokensUsed = 10 }),
                // Turn 2: streams the final answer.
                new StreamedTurn(Deltas: ["done"], Final: new LlmResponse { Content = "done", TokensUsed = 5 }),
            ]);
        var budget = new Orkeon.Domain.Autonomous.AgentExecutionBudget { MaxToolCalls = 5, MaxTokensConsumed = 1000 };
        var gate = new AllowAllGate();

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget, gate);

        engine.SetValue("__deltas", new List<object>());
        var options = BuildOptions(engine, "({ onDelta: d => __deltas.push(d), permissionMode: \"acceptEdits\" })");
        var result = await facade.act("read then answer", options);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
        Assert.Equal(1, budget.CurrentToolCalls);
        Assert.Equal(15, budget.CurrentTokensConsumed);
        Assert.Equal("acceptEdits", gate.LastMode);
        var deltas = (List<object>)engine.GetValue("__deltas").ToObject()!;
        Assert.Equal(new object[] { "done" }, deltas.ToArray());
    }

    [Fact]
    public async Task Act_without_onDelta_keeps_the_buffered_chat_path()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns: [new StreamedTurn(Deltas: ["x"], Final: new LlmResponse { Content = "x" })])
        {
            BufferedResponse = new LlmResponse { Content = "buffered" },
        };

        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var result = await facade.act("go", null);

        Assert.Equal("buffered", result.Get("output").AsString());
        Assert.Equal(0, provider.ChatStreamingCalls);
        Assert.Equal(1, provider.ChatCalls);
    }

    // ── helpers & fakes ──────────────────────────────────────────────────────

    private static Jint.Native.JsValue BuildOptions(Engine engine, string js) => engine.Evaluate(js);

    private sealed record StreamedTurn(string[] Deltas, LlmResponse Final);

    private sealed class FakeStreamingProvider : ILlmProvider, IStreamingLlmProvider
    {
        private readonly string[] _generateChunks;
        private readonly StreamedTurn[] _turns;
        private int _turn;

        public int ChatCalls { get; private set; }
        public int ChatStreamingCalls { get; private set; }
        public LlmResponse BufferedResponse { get; set; } = new() { Content = "buffered" };

        public FakeStreamingProvider(string[] generateChunks, StreamedTurn[] turns)
        {
            _generateChunks = generateChunks;
            _turns = turns;
        }

        public string Name => "fake-streaming";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "fake-model" };
        public bool SupportsStreaming => true;

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = string.Concat(_generateChunks) });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            ChatCalls++;
            return Task.FromResult(BufferedResponse);
        }

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var chunk in _generateChunks)
                yield return chunk;
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            ChatStreamingCalls++;
            var turn = _turns[Math.Min(_turn++, _turns.Length - 1)];
            foreach (var delta in turn.Deltas)
                yield return LlmStreamEvent.Content(delta);
            yield return LlmStreamEvent.Complete(turn.Final);
            await Task.CompletedTask;
        }
    }

    private sealed class PlainProvider : ILlmProvider
    {
        private readonly string _content;
        public PlainProvider(string content) => _content = content;
        public string Name => "plain";
        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = _content });
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = _content });
    }

    private sealed class AllowAllGate : IPermissionGate
    {
        public string? LastMode { get; private set; }
        public Task<PermissionVerdict> CheckAsync(
            string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
            CancellationToken cancellationToken = default)
        {
            LastMode = mode;
            return Task.FromResult(PermissionVerdict.Allow());
        }
    }

    private sealed class RecordingTool : IBaseTool
    {
        public RecordingTool(string name) => Name = name;
        public string Name { get; }
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());
        public int CallCount { get; private set; }

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
