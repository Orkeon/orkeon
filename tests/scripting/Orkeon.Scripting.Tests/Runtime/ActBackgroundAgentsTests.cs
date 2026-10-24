using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-02: a script starts background agents, receives each completion once, waits for them, under a crew-wide gate.</summary>
public sealed class ActBackgroundAgentsTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    private const string Options = "waitFor: \"children\", maxIterations: 20";

    private static string Crew(string crewOptions, string actOptions) => """
        const kid = (n) => agentBuilder().name(n).role("R").goal("G")
            .body(async (input, ctx) => (await ctx.llm.act(String(input), { permissionMode: "bypassPermissions" })).output)
            .build();
        const root = agentBuilder().name("root").role("R").goal("G")
            .body(async (input, ctx) => {
                ctx.spawnBackground("a", "child-a");
                ctx.spawnBackground("b", "child-b");
                return (await ctx.llm.act("orchestrate", { permissionMode: "bypassPermissions",
        """ + actOptions + """
         })).output;
            }).build();
        crewBuilder().name("c")
        """ + crewOptions + ".withAgent(root).withAgent(kid(\"a\")).withAgent(kid(\"b\")).build();";

    private static async Task<Task> StartAsync(SlowLlmProvider provider, string crewOptions, string actOptions)
    {
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(Crew(crewOptions, actOptions), null, TestContext.Current.CancellationToken)).ToObject()!;
        return crew.RunAsync(null, TestContext.Current.CancellationToken);
    }

    /// <summary>Waits for the signals, or surfaces the run's own fault when it ends first.</summary>
    private static async Task AwaitSignalsAsync(Task run, params Task[] signals)
    {
        var all = Task.WhenAll(signals).WaitAsync(Safety);
        if (await Task.WhenAny(all, run) == run) await run;
        await all;
    }

    private static List<LlmMessage[]> Orchestrator(SlowLlmProvider p)
        => p.Chats.Where(c => c.Any(m => m.Role == "user" && m.Content == "orchestrate")).ToList();

    private static List<string> UserMessages(LlmMessage[] chat)
        => chat.Where(m => m.Role == "user").Select(m => m.Content).ToList();

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldRunTwoBackgroundAgentsConcurrently_WhenSpawnedFromOneTurn()
    {
        var provider = new SlowLlmProvider();
        provider.Hold("child-a");
        provider.Hold("child-b");

        var run = await StartAsync(provider, "", Options);
        await AwaitSignalsAsync(run, provider.Entered("child-a"), provider.Entered("child-b"));

        Assert.Equal(2, provider.MaxActive);
        provider.Release("child-a");
        provider.Release("child-b");
        await run.WaitAsync(Safety, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldDeliverEachCompletionAsMessage_WhenChildrenFinish()
    {
        var provider = new SlowLlmProvider();
        provider.FailOn("child-b");

        var run = await StartAsync(provider, "", Options);
        await run.WaitAsync(Safety, TestContext.Current.CancellationToken);

        var last = UserMessages(Orchestrator(provider)[^1]);
        Assert.Single(last, "agent a completed: R:child-a");
        var failure = Assert.Single(last, m => m.Contains("agent b", StringComparison.Ordinal));
        Assert.Contains("failed", failure, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldEndWait_WhenEveryTicketDelivered()
    {
        var provider = new SlowLlmProvider();

        var run = await StartAsync(provider, "", Options);
        await run.WaitAsync(Safety, TestContext.Current.CancellationToken);

        var turns = Orchestrator(provider);
        Assert.InRange(turns.Count, 1, 19);
        var last = UserMessages(turns[^1]);
        Assert.Single(last, "agent a completed: R:child-a");
        Assert.Single(last, "agent b completed: R:child-b");
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldBoundConcurrency_WhenCrewConcurrencyIsOne()
    {
        var provider = new SlowLlmProvider();
        provider.Hold("child-a");
        provider.Hold("child-b");
        var entered = new Dictionary<string, Task> { ["child-a"] = provider.Entered("child-a"), ["child-b"] = provider.Entered("child-b") };

        var run = await StartAsync(provider, ".concurrency(1)", Options);
        await AwaitSignalsAsync(run, Task.WhenAny(entered.Values));
        var first = entered.First(e => e.Value.IsCompleted).Key;
        var second = first == "child-a" ? "child-b" : "child-a";


        provider.Release(first);
        await AwaitSignalsAsync(run, entered[second]);
        provider.Release(second);
        await run.WaitAsync(Safety, TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.MaxActive);

        var engine = new JsEngineFactory(llmProvider: new SlowLlmProvider()).Create();
        var refused = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await engine.EvaluateAsync(Crew(".concurrency(0)", ""), null, TestContext.Current.CancellationToken));
        Assert.Contains("concurrency", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string KidAgent = """
        const kid = (n) => agentBuilder().name(n).role("R").goal("G")
            .body(async (input, ctx) => (await ctx.llm.act(String(input), { permissionMode: "bypassPermissions" })).output)
            .build();
        """;

    private static async Task<Task> StartBodyAsync(SlowLlmProvider provider, string rootBody)
    {
        var js = KidAgent + "const root = agentBuilder().name(\"root\").role(\"R\").goal(\"G\").body(async (input, ctx) => {"
            + rootBody + "}).build(); crewBuilder().name(\"c\").withAgent(root).withAgent(kid(\"a\")).build();";
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        return crew.RunAsync(null, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    [Trait("BR", "RT-02")]
    public async Task ShouldCompleteNestedSpawn_WhenCrewConcurrencyIsOne()
    {
        const string js = """
            const mk = (n) => agentBuilder().name(n).role("R").goal("G")
                .body(async (input, ctx) => (await ctx.llm.act(String(input ?? "go"), { permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().name("c").concurrency(1).budget({ delegationDepth: 3 })
                .withAgent(mk("root")).withAgent(mk("b")).withAgent(mk("c")).build();
            """;
        var provider = new FakeToolCallingLlmProvider(
            FakeToolCallingLlmProvider.ScriptStep.Call("spawn_agent", "{\"agent\":\"b\",\"input\":\"work\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Call("spawn_agent", "{\"agent\":\"c\",\"input\":\"work\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("C-out"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("B-final"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("ROOT-final"));
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal(2, provider.ToolResults.Count);
        Assert.Contains("C-out", provider.ToolResults[0], StringComparison.Ordinal);
        Assert.Contains("B-final", provider.ToolResults[1], StringComparison.Ordinal);
        Assert.Equal("ROOT-final", result.tasks[0].output!.ToString());
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldKeepMaxActiveAtOne_WhenHeldChildSpawnsBackgroundChildrenAndWaits()
    {
        const string js = """
            const kid = (n) => agentBuilder().name(n).role("R").goal("G")
                .body(async (input, ctx) => (await ctx.llm.act(String(input), { permissionMode: "bypassPermissions" })).output)
                .build();
            const mid = agentBuilder().name("b").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("x", "child-x");
                    ctx.spawnBackground("y", "child-y");
                    return (await ctx.llm.act("b-wait", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 })).output;
                }).build();
            const root = agentBuilder().name("root").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("b", "child-b");
                    return (await ctx.llm.act("orchestrate", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 })).output;
                }).build();
            crewBuilder().name("c").concurrency(1)
                .withAgent(root).withAgent(mid).withAgent(kid("x")).withAgent(kid("y")).build();
            """;
        var provider = new SlowLlmProvider();
        provider.Hold("child-x");
        provider.Hold("child-y");
        var entered = new Dictionary<string, Task> { ["child-x"] = provider.Entered("child-x"), ["child-y"] = provider.Entered("child-y") };
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        var run = crew.RunAsync(null, TestContext.Current.CancellationToken);

        await AwaitSignalsAsync(run, Task.WhenAny(entered.Values));
        var first = entered.First(e => e.Value.IsCompleted).Key;
        var second = first == "child-x" ? "child-y" : "child-x";
        provider.Release(first);
        await AwaitSignalsAsync(run, entered[second]);
        provider.Release(second);
        await run.WaitAsync(Safety, TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.MaxActive);
        var bChat = UserMessages(provider.Chats.Last(c => c.Any(m => m.Role == "user" && m.Content == "b-wait")));
        Assert.Single(bChat, "agent x completed: R:child-x");
        Assert.Single(bChat, "agent y completed: R:child-y");
        var rootChat = UserMessages(Orchestrator(provider)[^1]);
        Assert.Single(rootChat, m => m.StartsWith("agent b completed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldKeepChildRunningForNextAct_WhenTheFirstActDidNotStartIt()
    {
        var provider = new SlowLlmProvider();
        provider.Hold("child-a");
        provider.Hold("orchestrate");

        var run = await StartBodyAsync(provider, """
            ctx.spawnBackground("a", "child-a");
            await ctx.llm.act("q1", { permissionMode: "bypassPermissions" });
            return (await ctx.llm.act("orchestrate", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 })).output;
            """);
        await AwaitSignalsAsync(run, provider.Entered("child-a"), provider.Entered("orchestrate"));
        provider.Release("child-a");
        provider.Release("orchestrate");
        await run.WaitAsync(Safety, TestContext.Current.CancellationToken);

        var last = UserMessages(Orchestrator(provider)[^1]);
        Assert.Single(last, "agent a completed: R:child-a");
        Assert.DoesNotContain(last, m => m.Contains("agent a failed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldCancelChildStartedDuringAct_WhenActEnds()
    {
        var provider = new SlowLlmProvider();
        provider.Hold("child-a");
        provider.Hold("q1");

        var run = await StartBodyAsync(provider, """
            const pending = ctx.llm.act("q1", { permissionMode: "bypassPermissions" });
            ctx.spawnBackground("a", "child-a");
            await pending;
            return (await ctx.llm.act("orchestrate", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 })).output;
            """);
        await AwaitSignalsAsync(run, provider.Entered("child-a"));
        provider.Release("q1");
        await AwaitSignalsAsync(run, provider.Cancelled("child-a"));
        await run.WaitAsync(Safety, TestContext.Current.CancellationToken);

        Assert.True(provider.Cancelled("child-a").IsCompletedSuccessfully);
    }

    /// <summary>Runs a crew whose background child hands its context over, and returns the permit that child held.</summary>
    private static async Task<(GatePermit Permit, JsCrew Crew)> RunAndGrabChildPermitAsync()
    {
        const string js = """
            const kid = agentBuilder().name("a").role("R").goal("G")
                .body(async (input, ctx) => { grab(ctx); return (await ctx.llm.act(String(input), { permissionMode: "bypassPermissions" })).output; })
                .build();
            const root = agentBuilder().name("root").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("a", "child-a");
                    return (await ctx.llm.act("orchestrate", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 })).output;
                }).build();
            crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            """;
        var engine = new JsEngineFactory(llmProvider: new SlowLlmProvider()).Create();
        var grabbed = new System.Collections.Concurrent.ConcurrentBag<JsExecutionContext>();
        engine.SetValue("grab", new Action<object>(c => grabbed.Add((JsExecutionContext)c)));
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;

        await crew.RunAsync(null, TestContext.Current.CancellationToken).WaitAsync(Safety, TestContext.Current.CancellationToken);

        // The agent also runs as a crew member, holding no permit: only the child run's context has one.
        var permit = Assert.Single(grabbed.Select(c => c.Background.Permit), p => p is not null);
        return (permit!, crew);
    }

    [Fact]
    [Trait("BR", "R10")]
    public async Task ShouldClosePermit_WhenCrewRunReleasesItsGate()
    {
        var (permit, _) = await RunAndGrabChildPermitAsync();

        Assert.True(permit.IsClosed);
    }

    [Fact]
    [Trait("BR", "R10")]
    public async Task ShouldRefuseReacquire_WhenBackgroundChildOutlivesTheRun()
    {
        var (permit, crew) = await RunAndGrabChildPermitAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => permit.AcquireAsync(CancellationToken.None));
        Assert.Equal(4, crew.ConcurrencyGate.CurrentCount);
    }

    private static readonly TimeSpan Short = TimeSpan.FromSeconds(10);

    private static string HeldChildCrew(int concurrency, params string[] later)
    {
        var spawns = string.Concat(later.Select(n => $"ctx.spawnBackground(\"{n}\", \"child-{n}\");"));
        var agents = string.Concat(later.Select(n => $".withAgent(kid(\"{n}\"))"));
        return $$"""
            const kid = (n) => agentBuilder().name(n).role("R").goal("G")
                .body(async (input, ctx) => (await ctx.llm.act(String(input), { permissionMode: "bypassPermissions" })).output)
                .build();
            const mid = agentBuilder().name("b").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("x", "child-x");
                    ctx.spawnBackground("y", "child-y");
                    const w = (p) => ctx.llm.act(p, { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 });
                    const both = await Promise.all([w("b-wait1"), w("b-wait2")]);
                    return both.map(r => r.output).join("|");
                }).build();
            const later = agentBuilder().name("m").role("R").goal("G")
                .body(async (input, ctx) => {
                    {{spawns}}
                    return (await ctx.llm.act("orchestrate2", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 })).output;
                }).build();
            const root = agentBuilder().name("root").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("b", "child-b");
                    await ctx.llm.act("orchestrate", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 });
                    await ctx.llm.act("root-pause", { permissionMode: "bypassPermissions" });
                    ctx.spawnBackground("m", "child-m");
                    return (await ctx.llm.act("orchestrate3", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 20 })).output;
                }).build();
            crewBuilder().name("c").concurrency({{concurrency}})
                .withAgent(root).withAgent(mid).withAgent(later).withAgent(kid("x")).withAgent(kid("y")){{agents}}.build();
            """;
    }

    private static async Task<Task> StartHeldAsync(SlowLlmProvider provider, string js)
    {
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        return crew.RunAsync(null, TestContext.Current.CancellationToken);
    }

    private static async Task SignalsAsync(Task run, params Task[] signals)
    {
        var all = Task.WhenAll(signals).WaitAsync(Short);
        if (await Task.WhenAny(all, run) == run) await run;
        await all;
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task Two_concurrent_waiting_acts_in_a_held_child_complete_and_a_later_spawn_keeps_the_bound_at_concurrency_1()
    {
        var provider = new SlowLlmProvider();
        foreach (var k in new[] { "child-x", "child-y", "child-z" }) provider.Hold(k);
        var run = await StartHeldAsync(provider, HeldChildCrew(1, "z"));

        var entered = new Dictionary<string, Task> { ["child-x"] = provider.Entered("child-x"), ["child-y"] = provider.Entered("child-y") };
        await SignalsAsync(run, Task.WhenAny(entered.Values));
        var first = entered.First(e => e.Value.IsCompleted).Key;
        var second = first == "child-x" ? "child-y" : "child-x";
        provider.Release(first);
        await SignalsAsync(run, entered[second]);
        provider.Release(second);
        await SignalsAsync(run, provider.Entered("child-z"));
        provider.Release("child-z");
        await run.WaitAsync(Short, TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.MaxActive);
        var last = UserMessages(provider.Chats.Last(c => c.Any(m => m.Role == "user" && m.Content == "orchestrate2")));
        Assert.Single(last, "agent z completed: R:child-z");
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task Two_concurrent_waiting_acts_in_a_held_child_leave_the_gate_whole_at_concurrency_2()
    {
        var provider = new SlowLlmProvider();
        foreach (var k in new[] { "child-x", "child-y", "child-p", "child-q", "child-r" }) provider.Hold(k);
        var run = await StartHeldAsync(provider, HeldChildCrew(2, "p", "q", "r"));

        await SignalsAsync(run, provider.Entered("child-x"), provider.Entered("child-y"));
        provider.Release("child-x");
        provider.Release("child-y");

        var later = new[] { "child-p", "child-q", "child-r" };
        var enteredLater = later.ToDictionary(k => k, provider.Entered);
        await SignalsAsync(run, Task.WhenAny(enteredLater.Values));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var concurrently = enteredLater.Count(e => e.Value.IsCompleted);
        foreach (var k in later) provider.Release(k);
        await run.WaitAsync(Short, TestContext.Current.CancellationToken);

        Assert.Equal(2, concurrently);
        Assert.Equal(2, provider.MaxActive);
    }
}
