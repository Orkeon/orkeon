using System.Diagnostics;
using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Telemetry;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests.Telemetry;

public sealed class OtelTests
{
    private static (List<Activity> spans, ActivityListener listener) Capture()
    {
        var captured = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == ScriptingActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                lock (captured) captured.Add(a);
            },
        };
        ActivitySource.AddActivityListener(listener);
        return (captured, listener);
    }

    private static List<Activity> Snapshot(List<Activity> spans)
    {
        lock (spans) return spans.ToList();
    }

    [Fact]
    public async Task crew_run_emits_a_crew_run_span_with_name_and_process_tags()
    {
        var (spans, listener) = Capture();
        try
        {
            var engine = new JsEngineFactory().Create();
            var crew = (JsCrew)(await engine.EvaluateAsync("""
                const a = agentBuilder().name("A").role("R").goal("G").body(() => "ok").build();
                crewBuilder().name("test-crew").process("sequential").withAgent(a).build();
                """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;

            await crew.RunAsync(null, CancellationToken.None);

            var crewSpan = Snapshot(spans)
                .Where(s => s.OperationName == ScriptingActivitySource.CrewRunSpan)
                .FirstOrDefault(s => s.Tags.Any(kv => kv.Key == "orkeon.crew.name" && kv.Value == "test-crew"));
            Assert.NotNull(crewSpan);
            Assert.Contains(crewSpan!.Tags, kv => kv.Key == "orkeon.crew.process" && kv.Value == "sequential");
        }
        finally { listener.Dispose(); }
    }

    [Fact]
    public async Task agent_run_emits_a_child_span_under_crew_run()
    {
        var (spans, listener) = Capture();
        try
        {
            var engine = new JsEngineFactory().Create();
            var crew = (JsCrew)(await engine.EvaluateAsync("""
                const a = agentBuilder().name("Worker-Otel").role("R").goal("G").body(() => "ok").build();
                crewBuilder().withAgent(a).build();
                """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;

            await crew.RunAsync(null, CancellationToken.None);

            var agentSpan = Snapshot(spans)
                .Where(s => s.OperationName.StartsWith(ScriptingActivitySource.AgentRunSpan, StringComparison.Ordinal))
                .FirstOrDefault(s => s.Tags.Any(kv => kv.Key == "gen_ai.agent.name" && kv.Value == "Worker-Otel"));
            Assert.NotNull(agentSpan);
        }
        finally { listener.Dispose(); }
    }

    [Fact]
    public async Task llm_call_emits_a_span_with_method_and_prompt_length_tags()
    {
        var (spans, listener) = Capture();
        try
        {
            var provider = new StubLlmProvider().RespondWith(new LlmResponse { Content = "ok", TokensUsed = 7 });
            using var engine = new Engine();
            var facade = new JsLlmFacade(engine, provider, CancellationToken.None);

            // A prompt whose LENGTH no other test in this assembly produces: the
            // ActivityListener is process-global, and this test used to take the first
            // llm-call span it saw. Under a parallel run that was somebody else's — a
            // concurrent act() loop, whose span is tagged orkeon.llm.method=act — and the
            // assertion failed on a span this test never emitted (same idiom as
            // crew_run/agent_run/tool_call above, which all discriminate by tag).
            const string Prompt = "otel-probe-unique-prompt-length";

            await facade.complete(Prompt, null);

            var span = Snapshot(spans)
                .Where(s => s.OperationName.StartsWith(ScriptingActivitySource.LlmCallSpan, StringComparison.Ordinal))
                .FirstOrDefault(s => s.TagObjects.Any(
                    kv => kv.Key == "orkeon.llm.prompt.length" && Equals(kv.Value, Prompt.Length)));
            Assert.NotNull(span);
            Assert.Contains(span!.Tags, kv => kv.Key == "orkeon.llm.method" && kv.Value == "complete");
        }
        finally { listener.Dispose(); }
    }

    [Fact]
    public async Task tool_call_emits_a_span_with_tool_name_tag()
    {
        var (spans, listener) = Capture();
        try
        {
            var tool = new StubBaseTool("file_read").RespondWithSuccess("ok");
            var engine = new JsEngineFactory(builtInTools: [(Orkeon.Domain.Tools.IBaseTool)tool]).Create();

            await Task.Run(() => engine.Evaluate("tools.fileRead({})").UnwrapIfPromise());

            // Filter by the unique tool.name tag — the ActivityListener is process-global,
            // so a concurrent test emitting another tool-call span would otherwise be picked
            // up here (same idiom as crew_run/agent_run above).
            var span = Snapshot(spans)
                .Where(s => s.OperationName.StartsWith(ScriptingActivitySource.ToolCallSpan, StringComparison.Ordinal))
                .FirstOrDefault(s => s.Tags.Any(kv => kv.Key == "gen_ai.tool.name" && kv.Value == "file_read"));
            Assert.NotNull(span);
        }
        finally { listener.Dispose(); }
    }

    [Fact]
    public async Task no_listener_means_no_spans_are_materialised()
    {
        var engine = new JsEngineFactory().Create();
        var crew = (JsCrew)(await engine.EvaluateAsync("""
            const a = agentBuilder().name("A").role("R").goal("G").body(() => "x").build();
            crewBuilder().withAgent(a).build();
            """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;

        await crew.RunAsync(null, CancellationToken.None);

        Assert.Null(Activity.Current);
    }
}
