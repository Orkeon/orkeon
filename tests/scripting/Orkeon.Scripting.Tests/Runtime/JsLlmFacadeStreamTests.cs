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
        await foreach (var c in facade.StreamChunks("hi", null))
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
        await foreach (var c in facade.StreamChunks("hi", null))
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

    [Fact]
    public async Task Act_with_native_sink_streams_without_onDelta()
    {
        // F5 L3: a host-registered sink is enough to switch act() to the streaming path —
        // no script-side onDelta required (native REPL rendering).
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns: [new StreamedTurn(Deltas: ["Hel", "lo"], Final: new LlmResponse { Content = "Hello" })]);
        var sink = new RecordingSink();

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, tools: null, budget: null,
            permissionGate: null, observability: new JsLlmObservability { DeltaSink = sink });

        var result = await facade.act("go", null);

        Assert.Equal("Hello", result.Get("output").AsString());
        Assert.Equal(1, provider.ChatStreamingCalls);
        Assert.Equal(0, provider.ChatCalls);
        Assert.Equal(["Hel", "lo"], sink.Deltas);
        Assert.Equal(1, sink.TurnsCompleted);
    }

    [Fact]
    public async Task Act_sink_and_onDelta_both_receive_every_delta()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns: [new StreamedTurn(Deltas: ["a", "b"], Final: new LlmResponse { Content = "ab" })]);
        var sink = new RecordingSink();

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, tools: null, budget: null,
            permissionGate: null, observability: new JsLlmObservability { DeltaSink = sink });

        engine.SetValue("__deltas", new List<object>());
        var options = BuildOptions(engine, "({ onDelta: d => __deltas.push(d) })");
        var result = await facade.act("go", options);

        Assert.Equal("ab", result.Get("output").AsString());
        Assert.Equal(["a", "b"], sink.Deltas);
        var jsDeltas = (List<object>)engine.GetValue("__deltas").ToObject()!;
        Assert.Equal(new object[] { "a", "b" }, jsDeltas.ToArray());
    }

    [Fact]
    public async Task Sink_turn_terminator_is_skipped_for_toolcall_only_turns()
    {
        // A turn that streams no visible content (pure tool call) must not emit the line
        // terminator — otherwise every tool call would inject a blank line in the REPL.
        using var engine = new Engine();
        var tool = new RecordingTool("file_read");
        var toolCallBody =
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
            "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"file_read\",\"arguments\":\"{}\"}}]}}]}";
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns:
            [
                new StreamedTurn(Deltas: [], Final: new LlmResponse { Content = "", RawResponseBody = toolCallBody }),
                new StreamedTurn(Deltas: ["done"], Final: new LlmResponse { Content = "done" }),
            ]);
        var sink = new RecordingSink();

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, budget: null,
            permissionGate: null, observability: new JsLlmObservability { DeltaSink = sink });

        var result = await facade.act("read then answer", null);

        Assert.Equal("done", result.Get("output").AsString());
        Assert.Equal(1, tool.CallCount);
        Assert.Equal(["done"], sink.Deltas);
        Assert.Equal(1, sink.TurnsCompleted); // only the visible turn terminated a line
    }

    [Fact]
    public async Task Sink_with_non_streaming_provider_keeps_the_buffered_path()
    {
        using var engine = new Engine();
        var provider = new PlainProvider("buffered answer");
        var sink = new RecordingSink();

        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, tools: null, budget: null,
            permissionGate: null, observability: new JsLlmObservability { DeltaSink = sink });

        var result = await facade.act("go", null);

        Assert.Equal("buffered answer", result.Get("output").AsString());
        Assert.Empty(sink.Deltas);
        Assert.Equal(0, sink.TurnsCompleted);
    }

    // ── helpers & fakes ──────────────────────────────────────────────────────

    private static Jint.Native.JsValue BuildOptions(Engine engine, string js) => engine.Evaluate(js);

    // ── The streaming surface `ctx.llm.stream` actually takes (2026-08-04) ──
    //
    // It used to take `GenerateStreamingAsync`, which reads the same SSE stream but
    // asks for less and keeps less: no `stream_options.include_usage` (so most
    // providers report NO usage for a streamed call) and `delta.content` only (so a
    // thinking model's reasoning is dropped, and the stream is silent for as long as
    // it thinks). Both surfaces yield the same visible text, which is why the
    // difference needs its own tests.
    //
    // Usage and reasoning reach the caller through CLR state exposed on the object
    // `stream()` returns, NOT through JS callbacks. See
    // JsLlmFacadeConcurrentStreamTests for what callbacks cost: they re-enter the
    // single-threaded Jint engine from the stream loop, which killed a real round.

    [Fact]
    public async Task Stream_takes_the_chat_streaming_surface_not_the_plain_one()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(generateChunks: HelloChunks, turns: NoTurns);
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        await foreach (var _ in facade.StreamChunks("hi", null)) { }

        Assert.Equal(1, provider.ChatStreamingCalls);
        Assert.Equal(0, provider.GenerateStreamingCalls);
    }

    [Fact]
    public void Stream_exposes_the_terminal_usage_on_the_returned_object()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns:
            [
                new StreamedTurn(
                    Deltas: ["ans", "wer"],
                    Final: new LlmResponse
                    {
                        Content = "answer",
                        TokensUsed = 90,
                        PromptTokens = 30,
                        CompletionTokens = 60,
                        Model = "fake-model",
                    }),
            ]);
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var script = engine.Evaluate("""
            (async function () {
                let text = '';
                const s = llm.stream('hi');
                for await (const c of s) text += c;
                return text + '|' + s.usage.promptTokens + '|' + s.usage.completionTokens
                     + '|' + s.usage.tokensUsed + '|' + s.usage.model;
            })()
            """);
        var result = Unwrap(engine, script).AsString();

        Assert.Equal("answer|30|60|90|fake-model", result);
    }

    [Fact]
    public void Stream_counts_reasoning_deltas_and_never_yields_them_as_chunks()
    {
        // A reasoning delta is not part of the answer. A caller writing chunks to a
        // file must not find the model's scratchpad in it.
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns:
            [
                new StreamedTurn(
                    Deltas: ["visible"],
                    Final: new LlmResponse { Content = "visible" },
                    ReasoningDeltas: ["think", "ing"]),
            ]);
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var script = engine.Evaluate("""
            (async function () {
                let text = '';
                const s = llm.stream('hi');
                for await (const c of s) text += c;
                return text + '|' + s.reasoningChunks;
            })()
            """);
        var result = Unwrap(engine, script).AsString();

        Assert.Equal("visible|2", result);
    }

    [Fact]
    public async Task Stream_yields_only_the_visible_content()
    {
        // The reasoning deltas must be dropped from the chunk sequence, not appended
        // to the text as a fallback.
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(
            generateChunks: NoChunks,
            turns:
            [
                new StreamedTurn(
                    Deltas: ["visible"],
                    Final: new LlmResponse { Content = "visible" },
                    ReasoningDeltas: ["scratch"]),
            ]);
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

        var chunks = new List<string>();
        await foreach (var c in facade.StreamChunks("hi", null))
            chunks.Add(c);

        Assert.Equal(["visible"], chunks);
    }

    [Fact]
    public void Stream_reports_no_usage_rather_than_zeros_when_the_provider_reported_none()
    {
        // "this provider reported no usage" and "this call used 0 tokens" must not be
        // the same observation — the same rule as exp02's cache report.
        using var engine = new Engine();
        var provider = new PlainProvider("full text");
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var script = engine.Evaluate("""
            (async function () {
                let text = '';
                const s = llm.stream('hi');
                for await (const c of s) text += c;
                return text + '|' + (s.usage === null ? 'null' : 'set') + '|' + s.reasoningChunks;
            })()
            """);
        var result = Unwrap(engine, script).AsString();

        Assert.Equal("full text|null|0", result);
    }

    [Fact]
    public void Stream_carries_usage_on_the_non_streaming_fallback_when_the_provider_reports_it()
    {
        // The side-channel must not quietly stop working on a provider without SSE.
        using var engine = new Engine();
        var provider = new PlainProvider("full text") { Usage = new LlmResponse
        {
            Content = "full text", TokensUsed = 12, PromptTokens = 4, CompletionTokens = 8,
        } };
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var script = engine.Evaluate("""
            (async function () {
                const s = llm.stream('hi');
                for await (const c of s) { }
                return s.usage.promptTokens + '|' + s.usage.completionTokens;
            })()
            """);
        var result = Unwrap(engine, script).AsString();

        Assert.Equal("4|8", result);
    }

    private sealed record StreamedTurn(string[] Deltas, LlmResponse Final, string[]? ReasoningDeltas = null)
    {
        /// <summary>Reasoning deltas emitted BEFORE the content deltas, as a thinking model does.</summary>
        public string[] Reasoning => ReasoningDeltas ?? [];
    }

    private sealed class FakeStreamingProvider : ILlmProvider, IStreamingLlmProvider
    {
        private readonly string[] _generateChunks;
        private readonly StreamedTurn[] _turns;
        private int _turn;

        public int ChatCalls { get; private set; }
        public int ChatStreamingCalls { get; private set; }

        /// <summary>
        /// Counted so a test can pin WHICH streaming surface <c>ctx.llm.stream</c>
        /// takes. It used to take this one, which asks for no usage and drops
        /// reasoning deltas; a regression would be invisible otherwise, since both
        /// surfaces yield the same visible text.
        /// </summary>
        public int GenerateStreamingCalls { get; private set; }
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
            GenerateStreamingCalls++;
            foreach (var chunk in _generateChunks)
                yield return chunk;
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            ChatStreamingCalls++;
            if (_turns.Length == 0)
            {
                // No scripted turn: replay the generate chunks so a test that only
                // cares about the visible text does not have to script a turn.
                foreach (var chunk in _generateChunks)
                    yield return LlmStreamEvent.Content(chunk);
                yield return LlmStreamEvent.Complete(new LlmResponse { Content = string.Concat(_generateChunks) });
                await Task.CompletedTask;
                yield break;
            }
            var turn = _turns[Math.Min(_turn++, _turns.Length - 1)];
            foreach (var reasoning in turn.Reasoning)
                yield return LlmStreamEvent.Reasoning(reasoning);
            foreach (var delta in turn.Deltas)
                yield return LlmStreamEvent.Content(delta);
            yield return LlmStreamEvent.Complete(turn.Final);
            await Task.CompletedTask;
        }
    }

    private sealed class RecordingSink : ILlmDeltaSink
    {
        public List<string> Deltas { get; } = new();
        public int TurnsCompleted { get; private set; }
        public void OnDelta(string delta) => Deltas.Add(delta);
        public void OnTurnCompleted() => TurnsCompleted++;
    }

    private sealed class PlainProvider : ILlmProvider
    {
        private readonly string _content;
        public PlainProvider(string content) => _content = content;
        public string Name => "plain";

        /// <summary>Optional response with counts, for the non-streaming usage path.</summary>
        public LlmResponse? Usage { get; init; }

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(Usage ?? new LlmResponse { Content = _content });
        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(Usage ?? new LlmResponse { Content = _content });
    }

    private sealed class AllowAllGate : IPermissionGate
    {
        public string? LastMode { get; private set; }
        public Task<PermissionVerdict> CheckAsync(
            string toolName, IReadOnlyDictionary<string, object?> arguments, string mode,
            Orkeon.Domain.Tools.ToolAccess declaredAccess = Orkeon.Domain.Tools.ToolAccess.Unspecified,
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

    // The test the C# ones above could not be: it drives `for await` from SCRIPT
    // code. Measured 2026-08-03, before the async-iterable adapter landed, this
    // failed with "The value is not iterable" while every C# stream test was
    // green — `ctx.llm.stream` returned a bare CLR IAsyncEnumerable, which Jint
    // sees as an interop object carrying neither Symbol.asyncIterator nor
    // Symbol.iterator, and the ambient typings claimed AsyncIterable<string>.
    [Fact]
    public void Stream_is_async_iterable_from_script_code()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(generateChunks: HelloChunks, turns: NoTurns);
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var probe = engine.Evaluate(
            "(async function () { const it = llm.stream('hi'); "
            + "if (!it[Symbol.asyncIterator]) return 'NO_SYMBOL'; "
            + "const acc = []; for await (const c of it) acc.push(c); "
            + "return acc.join('|'); })");
        var result = Unwrap(engine, engine.Invoke(probe));

        Assert.Equal(string.Join('|', HelloChunks), result.AsString());
    }

    // A `break` inside `for await` calls the iterator's `return()`. Without it the
    // underlying provider read would be abandoned without disposal.
    [Fact]
    public void Stream_stops_early_and_releases_the_iterator_on_break()
    {
        using var engine = new Engine();
        var provider = new FakeStreamingProvider(generateChunks: HelloChunks, turns: NoTurns);
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None);
        engine.SetValue("llm", facade);

        var probe = engine.Evaluate(
            "(async function () { const it = llm.stream('hi'); "
            + "const acc = []; for await (const c of it) { acc.push(c); break; } "
            + "const after = await it[Symbol.asyncIterator]().next(); "
            + "return acc.length + ':' + String(after.done); })");
        var result = Unwrap(engine, engine.Invoke(probe));

        // One chunk consumed, and a fresh pull on a released sequence reports done.
        Assert.Equal("1:true", result.AsString());
    }

    // Drains the promise returned by an async JS function so the assertion sees a
    // settled value rather than a pending Promise object.
    private static Jint.Native.JsValue Unwrap(Engine engine, Jint.Native.JsValue value)
        => value.IsPromise() ? value.UnwrapIfPromise() : value;
}
