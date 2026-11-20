using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Security;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;
using static Orkeon.Scripting.Tests.Doubles.FakeToolCallingLlmProvider;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-01: the script host offers one <c>spawn_agent</c> tool that runs a crew agent as a child.</summary>
public sealed class SpawnAgentScriptToolTests
{
    private static string Crew(string budget) => """
        const mk = (n) => agentBuilder().name(n).role("R").goal("G")
            .body(async (input, ctx) => (await ctx.llm.act(String(input ?? "go"), { permissionMode: "bypassPermissions" })).output)
            .build();
        crewBuilder().name("c")
        """ + budget + ".withAgent(mk(\"root\")).withAgent(mk(\"b\")).withAgent(mk(\"c\")).withAgent(mk(\"d\")).build();";

    private static string Spawn(string agent) => "{\"agent\":\"" + agent + "\",\"input\":\"work\"}";

    private static Task<(FakeToolCallingLlmProvider Provider, string Output)> RunAsync(string budget, params ScriptStep[] script)
        => RunCrewAsync(Crew(budget), script);

    private static async Task<(FakeToolCallingLlmProvider Provider, string Output)> RunCrewAsync(string js, params ScriptStep[] script)
    {
        var provider = new FakeToolCallingLlmProvider(script);
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);
        return (provider, result.tasks[0].output!.ToString()!);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldOfferExactlyOneSpawnAgentTool_WhenCrewHasNamedAgents()
    {
        var run = await RunAsync("", ScriptStep.Answer("done"));

        Assert.Equal(["spawn_agent"], run.Provider.OfferedToolsPerTurn[0]);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldReturnEachChildOutput_WhenSpawnToolCalledThreeLevelsDeepWithDepthBudgetThree()
    {
        var run = await RunAsync(".budget({ delegationDepth: 3 })",
            ScriptStep.Call("spawn_agent", Spawn("b")),
            ScriptStep.Call("spawn_agent", Spawn("c")),
            ScriptStep.Call("spawn_agent", Spawn("d")),
            ScriptStep.Answer("D-out"),
            ScriptStep.Answer("C-final"),
            ScriptStep.Answer("B-final"),
            ScriptStep.Answer("ROOT-final"));

        Assert.Equal(3, run.Provider.ToolResults.Count);
        Assert.Contains("D-out", run.Provider.ToolResults[0], StringComparison.Ordinal);
        Assert.Contains("C-final", run.Provider.ToolResults[1], StringComparison.Ordinal);
        Assert.Contains("B-final", run.Provider.ToolResults[2], StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldRefuseSpawn_WhenDelegationDepthBudgetExceeded()
    {
        var run = await RunAsync("",
            ScriptStep.Call("spawn_agent", Spawn("b")),
            ScriptStep.Call("spawn_agent", Spawn("c")),
            ScriptStep.Call("spawn_agent", Spawn("d")),
            ScriptStep.Answer("C-final"),
            ScriptStep.Answer("B-final"),
            ScriptStep.Answer("ROOT-final"));

        Assert.Contains("delegation depth", run.Provider.ToolResults[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("D-out", run.Provider.ToolResults[0], StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldFeedTheRefusalBackAndFinishTheTurn_WhenSpawnBudgetIsSpent()
    {
        // #422: past the spawn budget the call is refused, the model told, and the turn goes on.
        var run = await RunAsync(".budget({ spawnedAgents: 1 })",
            ScriptStep.Call("spawn_agent", Spawn("b")),
            ScriptStep.Answer("B-final"),
            ScriptStep.Call("spawn_agent", Spawn("c")),
            ScriptStep.Answer("ROOT-final"));

        Assert.Equal(2, run.Provider.ToolResults.Count);
        Assert.Contains("B-final", run.Provider.ToolResults[0], StringComparison.Ordinal);
        Assert.Contains("Spawn budget spent", run.Provider.ToolResults[1], StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldFeedTheCrewsLimitBack_WhenTheCrewBudgetRefusesTheSpawn()
    {
        // #422: a run charged to the crew allows 5 spawns, the crew 1: the crew's limit refuses the second, the run goes on.
        const string js = """
            const mk = (n) => agentBuilder().name(n).role("R").goal("G")
                .body(async (input, ctx) => (await ctx.llm.act(String(input ?? "go"), { permissionMode: "bypassPermissions" })).output)
                .build();
            const root = agentBuilder().name("root").role("R").goal("G")
                .body(async (input, ctx) => await ctx.delegate("b", "work", { budget: { spawnedAgents: 5 }, charge: "crew" }))
                .build();
            crewBuilder().name("c").budget({ spawnedAgents: 1 })
                .withAgent(root).withAgent(mk("b")).withAgent(mk("c")).withAgent(mk("d")).build();
            """;
        var run = await RunCrewAsync(js,
            ScriptStep.Call("spawn_agent", Spawn("c")),
            ScriptStep.Answer("C-final"),
            ScriptStep.Call("spawn_agent", Spawn("d")),
            ScriptStep.Answer("B-final"));

        Assert.Equal(2, run.Provider.ToolResults.Count);
        Assert.Contains(
            "Spawn budget spent: 1 sub-agents already spawned (limit 1 spawnedAgents); 'd' was not spawned. Carry on with the results you hold.",
            run.Provider.ToolResults[1], StringComparison.Ordinal);
        Assert.Equal("B-final", run.Output);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldFinishTheTurn_WhenTheLastSpawnReachesTheBudget()
    {
        var run = await RunAsync(".budget({ spawnedAgents: 1 })",
            ScriptStep.Call("spawn_agent", Spawn("b")),
            ScriptStep.Answer("B-final"),
            ScriptStep.Answer("ROOT-final"));

        Assert.Contains("B-final", Assert.Single(run.Provider.ToolResults), StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldRefuseSpawn_WhenAgentNameUnknown()
    {
        var run = await RunAsync("",
            ScriptStep.Call("spawn_agent", Spawn("ghost")),
            ScriptStep.Answer("ROOT-final"));

        Assert.Single(run.Provider.ToolResults);
        Assert.Contains("ghost", run.Provider.ToolResults[0], StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }
    // SEC-7: a child spawned by spawn_agent runs under the stricter of its own mode and its spawner's.
    // Under plan or dontAsk the spawn itself is refused; under acceptEdits the spawn runs and the child's
    // execute call is put to the operator (denied here) although the child poses bypassPermissions.
    [Theory]
    [InlineData("plan", 0, 0)]
    [InlineData("dontAsk", 0, 0)]
    [InlineData("acceptEdits", 0, 1)]
    [InlineData("bypassPermissions", 1, 0)]
    public async Task ShouldHoldTheChildToItsSpawnersStricterMode_WhenSpawnedThroughSpawnAgent(string spawnerMode, int writes, int asks)
    {
        var provider = new FakeToolCallingLlmProvider(
            ScriptStep.Call("spawn_agent", Spawn("writer")),
            ScriptStep.Call("file_write", "{}"),
            ScriptStep.Answer("child-done"),
            ScriptStep.Answer("root-done"));
        var tool = new FakeWritingTool("file_write", ToolAccess.Execute);
        var channel = new StubToolApprovalChannel { Decision = Orkeon.Application.Interfaces.Security.ToolApprovalDecision.Deny };
        var engine = new JsEngineFactory(
            builtInTools: new IBaseTool[] { tool },
            llmProvider: provider,
            hostPorts: new ScriptingHostPorts { PermissionGate = new ModePermissionGate(channel) }).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync($$"""
            const root = agentBuilder().name("root").role("R").goal("G")
                .body(async (input, ctx) => (await ctx.llm.act("go", { permissionMode: "{{spawnerMode}}" })).output)
                .build();
            const writer = agentBuilder().name("writer").role("R").goal("G")
                .tools(["file_write"])
                .body(async (input, ctx) => (await ctx.llm.act(String(input), { permissionMode: "bypassPermissions" })).output)
                .build();
            crewBuilder().name("c").withAgent(root).withAgent(writer).build();
            """, null, TestContext.Current.CancellationToken)).ToObject()!;

        await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(writes, tool.CallCount);
        Assert.Equal(asks, channel.Asks.Count);
    }

    // agent.spawned: a scripted spawn reaches the run's event stream, as an observed spawn_agent call does.
    private static Task<MockRunEventSink> RunObservedAsync(string rootBody, params ScriptStep[] script)
        => RunObservedAsync(rootBody, crewOptions: "", script);

    private static async Task<MockRunEventSink> RunObservedAsync(string rootBody, string crewOptions, params ScriptStep[] script)
    {
        var events = new MockRunEventSink();
        var provider = new FakeToolCallingLlmProvider(script);
        var engine = new JsEngineFactory(llmProvider: provider, hostPorts: new ScriptingHostPorts { RunEvents = events }).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync($$"""
            const root = agentBuilder().name("root").role("Lead").goal("G")
                .body(async (input, ctx) => { {{rootBody}} })
                .build();
            const kid = agentBuilder().name("kid").role("Researcher").goal("G")
                .body(async (input, ctx) => "kid-done")
                .build();
            crewBuilder().name("c"){{crewOptions}}.withAgent(root).withAgent(kid).build();
            """, null, TestContext.Current.CancellationToken)).ToObject()!;
        await crew.RunAsync(null, TestContext.Current.CancellationToken);
        return events;
    }

    private static List<Orkeon.Application.Runs.RunEvent> Spawned(MockRunEventSink events)
        => events.Events.Where(e => e.Kind == "agent.spawned").ToList();

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldEmitOneAgentSpawnedEvent_WhenModelCallsSpawnAgentDuringAct()
    {
        var events = await RunObservedAsync(
            "return (await ctx.llm.act(\"go\", { permissionMode: \"bypassPermissions\" })).output;",
            ScriptStep.Call("spawn_agent", "{\"agent\":\"kid\",\"input\":\"secret-act-input\"}"),
            ScriptStep.Answer("root-done"));

        var spawned = Assert.Single(Spawned(events));
        Assert.Equal("Researcher", spawned.Payload!["role"]!.GetValue<string>());
        Assert.Equal("kid", spawned.Payload!["name"]!.GetValue<string>());
        Assert.False(spawned.Payload!.ContainsKey("reason"));
        Assert.False(string.IsNullOrEmpty(spawned.CorrelationId));
        Assert.DoesNotContain("secret-act-input", spawned.Payload!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldEmitOneAgentSpawnedEvent_WhenScriptSpawnsBackgroundAgent()
    {
        var events = await RunObservedAsync("ctx.spawnBackground(\"kid\", \"secret-bg-input\"); return \"root-done\";");

        var spawned = Assert.Single(Spawned(events));
        Assert.Equal("Researcher", spawned.Payload!["role"]!.GetValue<string>());
        Assert.Equal("kid", spawned.Payload!["name"]!.GetValue<string>());
        Assert.False(spawned.Payload!.ContainsKey("reason"));
        Assert.DoesNotContain("secret-bg-input", spawned.Payload!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldEmitNoAgentSpawnedEvent_WhenSpawnBudgetIsSpent()
    {
        // The budget's one spawn goes to the first child: the second is refused, and reports nothing.
        var events = await RunObservedAsync(
            "ctx.spawnBackground(\"kid\", \"first\");"
            + " try { ctx.spawnBackground(\"kid\", \"second\"); return \"spawned\"; } catch (e) { return \"refused\"; }",
            ".budget({ spawnedAgents: 1 })");

        Assert.Single(Spawned(events));
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldEmitNoAgentSpawnedEvent_WhenSpawnRefusedForUnknownAgent()
    {
        var events = await RunObservedAsync(
            "return (await ctx.llm.act(\"go\", { permissionMode: \"bypassPermissions\" })).output;",
            ScriptStep.Call("spawn_agent", "{\"agent\":\"ghost\",\"input\":\"x\"}"),
            ScriptStep.Answer("root-done"));

        Assert.Empty(Spawned(events));
    }

    // #426: a run that names no budget gives the spawn tool, and the child it starts, no wall clock,
    // tool-call or token cap of their own; the delegation-depth and spawn-count defaults stay.
    private static async Task<(FakeToolCallingLlmProvider Provider, string Output, MockRunEventSink Events)> RunUnbudgetedAsync(
        string js, ManualTimeProvider clock, Action<int>? beforeTurn, params ScriptStep[] script)
    {
        var events = new MockRunEventSink();
        var provider = new FakeToolCallingLlmProvider(script) { BeforeTurn = beforeTurn };
        var engine = new JsEngineFactory(llmProvider: provider, hostPorts: new ScriptingHostPorts { RunEvents = events }).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        crew.Clock = clock;
        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);
        return (provider, result.tasks[0].output!.ToString()!, events);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldSpawnThroughSpawnAgent_WhenNoBudgetIsNamedAndSixMinutesHavePassed()
    {
        var clock = new ManualTimeProvider();

        // Root's first turn comes six minutes in; the child's turn two hours after that.
        var run = await RunUnbudgetedAsync(Crew(""), clock,
            turn => clock.Advance(turn == 0 ? TimeSpan.FromMinutes(6) : turn == 1 ? TimeSpan.FromHours(2) : TimeSpan.Zero),
            ScriptStep.Call("spawn_agent", Spawn("b")),
            ScriptStep.Answer("B-final"),
            ScriptStep.Answer("ROOT-final"));

        Assert.Contains("B-final", Assert.Single(run.Provider.ToolResults), StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }

    [Fact]
    [Trait("BR", "CH-436")]
    public async Task ShouldSpawnThroughSpawnAgent_WhenTheBudgetNamesOnlySpawnsAndSixMinutesHavePassed()
    {
        // Arrange
        var clock = new ManualTimeProvider();

        // Act: root's first turn comes six minutes in; the child's turn two hours after that.
        var run = await RunUnbudgetedAsync(Crew(".budget({ spawnedAgents: 10 })"), clock,
            turn => clock.Advance(turn == 0 ? TimeSpan.FromMinutes(6) : turn == 1 ? TimeSpan.FromHours(2) : TimeSpan.Zero),
            ScriptStep.Call("spawn_agent", Spawn("b")),
            ScriptStep.Answer("B-final"),
            ScriptStep.Answer("ROOT-final"));

        // Assert
        Assert.Contains("B-final", Assert.Single(run.Provider.ToolResults), StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldSpawnInBackground_WhenNoBudgetIsNamedAndSixMinutesHavePassed()
    {
        var clock = new ManualTimeProvider();
        const string js = """
            const root = agentBuilder().name("root").role("Lead").goal("G")
                .body(async (input, ctx) => {
                    await ctx.llm.act("go", { permissionMode: "bypassPermissions" });
                    ctx.spawnBackground("kid", "work");
                    return "root-done";
                })
                .build();
            const kid = agentBuilder().name("kid").role("Researcher").goal("G")
                .body(async (input, ctx) => "kid-done")
                .build();
            crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            """;

        var run = await RunUnbudgetedAsync(js, clock, _ => clock.Advance(TimeSpan.FromMinutes(6)), ScriptStep.Answer("thought"));

        Assert.Equal("root-done", run.Output);
        Assert.Equal("kid", Assert.Single(Spawned(run.Events)).Payload!["name"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public async Task ShouldLetTheChildPassFifteenToolCalls_WhenNoBudgetIsNamed()
    {
        // AgentExecutionBudget.Default allows 15 tool calls: the child's sixteenth would have ended its run.
        const string js = """
            const mk = (n) => agentBuilder().name(n).role("R").goal("G")
                .body(async (input, ctx) => (await ctx.llm.act(String(input ?? "go"),
                    { permissionMode: "bypassPermissions", maxIterations: 20 })).output)
                .build();
            crewBuilder().name("c").withAgent(mk("root")).withAgent(mk("b")).build();
            """;
        var script = new List<ScriptStep> { ScriptStep.Call("spawn_agent", Spawn("b")) };
        for (var i = 0; i < 16; i++)
            script.Add(ScriptStep.Call("spawn_agent", "{\"agent\":\"nope\",\"input\":\"try-" + i + "\"}"));
        script.Add(ScriptStep.Answer("B-final"));
        script.Add(ScriptStep.Answer("ROOT-final"));

        var run = await RunUnbudgetedAsync(js, new ManualTimeProvider(), null, [.. script]);

        Assert.Equal(17, run.Provider.ToolResults.Count);
        Assert.Contains("B-final", run.Provider.ToolResults[^1], StringComparison.Ordinal);
        Assert.Equal("ROOT-final", run.Output);
    }

    [Fact]
    [Trait("BR", "RT-01")]
    public void ShouldKeepTheSpawnAndDepthDefaults_WhenNoBudgetIsNamed()
    {
        var budget = SpawnAgentScriptTool.Unbudgeted(new ManualTimeProvider());
        var defaults = AgentExecutionBudget.Default;

        Assert.Equal(defaults.MaxDelegationDepth, budget.MaxDelegationDepth);
        for (var i = 0; i < defaults.MaxSpawnedAgents; i++) budget.RecordSpawn();
        var refused = Assert.Throws<BudgetExhaustedException>(budget.RecordSpawn);
        Assert.Equal(BudgetDimension.SpawnedAgents, refused.Dimension);
    }
}
