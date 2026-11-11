using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Security;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
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

    private static async Task<(FakeToolCallingLlmProvider Provider, string Output)> RunAsync(string budget, params ScriptStep[] script)
    {
        var provider = new FakeToolCallingLlmProvider(script);
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(Crew(budget), null, TestContext.Current.CancellationToken)).ToObject()!;
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
    private static async Task<MockRunEventSink> RunObservedAsync(string rootBody, params ScriptStep[] script)
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
            crewBuilder().name("c").withAgent(root).withAgent(kid).build();
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
        Assert.DoesNotContain("secret-bg-input", spawned.Payload!.ToJsonString(), StringComparison.Ordinal);
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
}
