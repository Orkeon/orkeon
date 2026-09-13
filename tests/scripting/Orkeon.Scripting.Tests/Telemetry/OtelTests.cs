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
                crewBuilder().name("parent-otel").withAgent(a).build();
                """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;

            await crew.RunAsync(null, CancellationToken.None);

            var agentSpan = Snapshot(spans)
                .Where(s => s.OperationName.StartsWith(ScriptingActivitySource.AgentRunSpan, StringComparison.Ordinal))
                .FirstOrDefault(s => s.Tags.Any(kv => kv.Key == "gen_ai.agent.name" && kv.Value == "Worker-Otel"));
            Assert.NotNull(agentSpan);
            // The agent span is a child of its run's crew span (SCR-25 T4) — started with that span made
            // current for the call — not of whatever Activity.Current happened to be on the draining thread.
            var crewSpan = Snapshot(spans)
                .Where(s => s.OperationName == ScriptingActivitySource.CrewRunSpan)
                .FirstOrDefault(s => s.Tags.Any(kv => kv.Key == "orkeon.crew.name" && kv.Value == "parent-otel"));
            Assert.NotNull(crewSpan);
            Assert.Equal(crewSpan!.SpanId, agentSpan!.ParentSpanId);
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

    /// <summary>
    /// Two runs interleaved on one drainer stop their spans in an order that leaves the thread's
    /// <see cref="Activity.Current"/> on a span the other run already stopped — every later span would
    /// parent under it. The runtime unsticks it after each stop, so the script's next span (or its
    /// next crew run) starts from a live ancestor or from nothing. The closing order is a property of
    /// the script — the second body releases the first and waits to be released after the first run
    /// closed — not of the scheduler: the other order never creates the stuck span.
    /// </summary>
    [Fact]
    public async Task interleaved_crew_runs_leave_no_stopped_span_current()
    {
        var (_, listener) = Capture();
        try
        {
            var engine = new JsEngineFactory().Create();
            engine.SetValue("__current", new Func<string>(
                () => Activity.Current is { } a ? a.OperationName + (a.IsStopped ? "(stopped)" : "") : "none"));

            var seen = await engine.EvaluateAsync("""
                let release1, release2;
                const gate1 = new Promise(resolve => { release1 = resolve; });
                const gate2 = new Promise(resolve => { release2 = resolve; });
                const a1 = agentBuilder().name("A1").role("R").goal("G").body(async () => { await gate1; return "a1"; }).build();
                const a2 = agentBuilder().name("A2").role("R").goal("G").body(async () => { release1(); await gate2; return "a2"; }).build();
                const c1 = crewBuilder().name("otel-c1").withAgent(a1).build();
                const c2 = crewBuilder().name("otel-c2").withAgent(a2).build();
                (async () => {
                    const r1 = c1.run(), r2 = c2.run();
                    await r1;            // c1 closes while c2's body is open
                    release2();
                    await r2;
                    const after = __current();
                    await c1.run();
                    return after + "|" + __current();
                })()
                """, cancellationToken: TestContext.Current.CancellationToken);

            Assert.DoesNotContain("(stopped)", seen.AsString(), StringComparison.Ordinal);
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
