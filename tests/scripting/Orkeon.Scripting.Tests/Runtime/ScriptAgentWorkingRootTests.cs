using Jint;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>#186: a script agent's <c>.workingRoot(path)</c> holds its tools as RT-06's <c>working_root:</c> holds a declared agent's.</summary>
public sealed class ScriptAgentWorkingRootTests
{
    private static readonly TimeSpan Safety = HangGuards.EnginePromise;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Jint.Engine NewEngine(FakeRunScopeProbeTool probe)
    {
        var fileSystem = new FakeRightsFileSystemService().AddMount("/workspace", FileAccessRights.ReadWrite);
        return new JsEngineFactory(
            builtInTools: [probe],
            llmProvider: new FakeWriteOnceLlmProvider(),
            hostPorts: new ScriptingHostPorts { FileSystem = fileSystem }).Create();
    }

    private static async Task<JsCrew> BuildCrewAsync(Jint.Engine engine, string js)
        => (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;

    private static string CrewOf(string rootCall, string body) => $$"""
        const a = agentBuilder().name("a").role("R").goal("G").tools(["file_write"]){{rootCall}}
            .body(async (input, ctx) => { {{body}} })
            .build();
        crewBuilder().name("c").withAgent(a).build();
        """;

    private static async Task<FakeRunScopeProbeTool> RunAsync(string rootCall, string body)
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), CrewOf(rootCall, body));
        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);
        return probe;
    }

    private static string MessagesOf(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            messages.Add(e.Message);
        return string.Join(" | ", messages);
    }

    private const string Act = """return (await ctx.llm.act("go", { permissionMode: "bypassPermissions" })).output;""";

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRunActToolsUnderRoot_WhenAgentDeclaresWorkingRoot()
    {
        var probe = await RunAsync(""".workingRoot("/workspace/wt/a/")""", Act);

        Assert.Equal("/workspace/wt/a", Assert.Single(probe.Roots));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRunCodeToolsUnderRoot_WhenBodyCallsGlobalTools()
    {
        var probe = await RunAsync(""".workingRoot("/workspace/wt/a")""", "await tools.fileWrite({}); return 'ok';");

        Assert.Equal("/workspace/wt/a", Assert.Single(probe.Roots));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRunCodeToolsUnderRoot_WhenBodyCallsCtxTools()
    {
        var probe = await RunAsync(""".workingRoot("/workspace/wt/a")""", "await ctx.tools.fileWrite({}); return 'ok';");

        Assert.Equal("/workspace/wt/a", Assert.Single(probe.Roots));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldLeaveRunUnrooted_WhenAgentDeclaresNoRoot()
    {
        var probe = await RunAsync("", Act + "\n");
        var codeProbe = await RunAsync("", "await tools.fileWrite({}); return 'ok';");

        Assert.Null(Assert.Single(probe.Roots));
        Assert.Null(Assert.Single(codeProbe.Roots));
    }

    [Theory]
    [Trait("BR", "RT-06")]
    [InlineData("/nowhere")]
    [InlineData("workspace/wt/a")]
    [InlineData("/workspace/../nowhere")]
    public async Task ShouldRefuseRunAtStart_WhenRootIsNotUnderAMount(string root)
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), CrewOf($$""".workingRoot("{{root}}")""",
            "globalThis.bodyRan = true; " + Act));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, Ct).WaitAsync(Safety, Ct));

        Assert.Contains($"The agent working root '{root}' is not under any mount of the file system.", MessagesOf(ex), StringComparison.Ordinal);
        Assert.Empty(probe.Roots);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRefuseEveryRoot_WhenHostWiredNoFileSystem()
    {
        var engine = new JsEngineFactory(builtInTools: [new FakeRunScopeProbeTool()], llmProvider: new FakeWriteOnceLlmProvider()).Create();
        var crew = await BuildCrewAsync(engine, CrewOf(""".workingRoot("/workspace")""", Act));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, Ct).WaitAsync(Safety, Ct));

        Assert.Contains("is not under any mount of the file system", MessagesOf(ex), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRunChildUnderParentRoot_WhenSpawnedChildDeclaresNone()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), """
            const kid = agentBuilder().name("kid").role("R").goal("G")
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "kid"; }).build();
            const own = agentBuilder().name("own").role("R").goal("G").workingRoot("/workspace/own")
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "own"; }).build();
            const root = agentBuilder().name("root").role("R").goal("G").workingRoot("/workspace/wt/a")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("kid", "x");
                    ctx.spawnBackground("own", "y");
                    return (await ctx.llm.act("wait", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 5 })).output;
                }).build();
            crewBuilder().name("c").withAgent(root).withAgent(kid).withAgent(own).build();
            """);

        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);

        // The run also steps through kid (unrooted: no parent) and own on their own; spawned, kid runs
        // under its parent's root and own under its own.
        Assert.Equal([null, "/workspace/own", "/workspace/own", "/workspace/wt/a"], probe.Roots.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldSendUnderParentRoot_WhenInstanceWasSpawnedWithoutOwnRoot()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), """
            const kid = agentBuilder().name("kid").role("R").goal("G").tools(["file_write"])
                .body(async () => "kid").build();
            const root = agentBuilder().name("root").role("R").goal("G").workingRoot("/workspace/wt/a")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("kid", "x");
                    await ctx.llm.act("wait", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 5 });
                    return await c.send("a1", "more");
                }).build();
            const c = crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            c;
            """);

        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);

        // Only the send's bare act calls the tool: it runs under the root the spawn handed the instance.
        Assert.Equal("/workspace/wt/a", Assert.Single(probe.Roots));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldIgnoreRootOption_WhenScriptPassesOneToRun()
    {
        var probe = new FakeRunScopeProbeTool();
        var engine = NewEngine(probe);
        await engine.EvaluateAsync("""
            const a = agentBuilder().name("a").role("R").goal("G")
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "a"; }).build();
            globalThis.crew = crewBuilder().withAgent(a).build();
            """, null, Ct);

        await engine.EvaluateAsync("""(async () => await crew.run({ root: "/workspace/forged" }))()""", null, Ct);

        Assert.Null(Assert.Single(probe.Roots));
    }

    private const string TwoRootedAgents = """
        let started = 0; let go; const gate = new Promise(resolve => { go = resolve; });
        const arrive = async () => { if (++started === 2) go(); await gate; };
        const a = agentBuilder().name("A").role("R").goal("G").workingRoot("/workspace/a")
            .body(async (input, ctx) => { await arrive(); await CALL; return "a"; }).build();
        const b = agentBuilder().name("B").role("R").goal("G").workingRoot("/workspace/b")
            .body(async (input, ctx) => { await arrive(); await CALL; return "b"; }).build();
        globalThis.crew = crewBuilder().withAgent(a).withAgent(b).build();
        """;

    private const string RunBoth = """
        (async () => {
            const outcomes = await Promise.allSettled([crew.runAgent("A", {}), crew.runAgent("B", {})]);
            return outcomes.map(o => o.status === "fulfilled" ? o.value : String(o.reason?.message ?? o.reason)).join(" || ");
        })()
        """;

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldHoldEachAgentToItsRoot_WhenConcurrentBodiesCallCtxTools()
    {
        var probe = new FakeRunScopeProbeTool();
        var engine = NewEngine(probe);
        await engine.EvaluateAsync(TwoRootedAgents.Replace("CALL", "ctx.tools.fileWrite({})", StringComparison.Ordinal), null, Ct);

        var outcome = await engine.EvaluateAsync(RunBoth, null, Ct);

        Assert.Equal("a || b", outcome.AsString());
        Assert.Equal(["/workspace/a", "/workspace/b"], probe.Roots.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRefuseGlobalToolCall_WhenConcurrentAgentsHaveDifferentRoots()
    {
        var probe = new FakeRunScopeProbeTool();
        var engine = NewEngine(probe);
        await engine.EvaluateAsync(TwoRootedAgents.Replace("CALL", "tools.fileWrite({})", StringComparison.Ordinal), null, Ct);

        var outcome = (await engine.EvaluateAsync(RunBoth, null, Ct)).AsString();

        Assert.Contains("agents with different working roots run", outcome, StringComparison.Ordinal);
        Assert.Contains("ctx.tools.fileWrite", outcome, StringComparison.Ordinal);
        Assert.Empty(probe.Roots);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldMapWorkingRoot_WhenCrewRunsOnDeclarativeShape()
    {
        var crew = await BuildCrewAsync(NewEngine(new FakeRunScopeProbeTool()), """
            const a = agentBuilder().name("a").role("R").goal("G").workingRoot("/workspace/wt/a").build();
            const b = agentBuilder().name("b").role("R").goal("G").build();
            crewBuilder().name("c").withAgent(a).withAgent(b).build();
            """);

        var agents = JsCrewConfigurationAdapter.ToConfiguration(crew).Agents;

        Assert.Equal("/workspace/wt/a", agents[0].WorkingRoot);
        Assert.Null(agents[1].WorkingRoot);
    }

    [Theory]
    [Trait("BR", "RT-06")]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldRefuseEmptyRoot_WhenBuilderGetsBlankPath(string path)
    {
        var builder = new Orkeon.Scripting.Builders.JsAgentBuilder();

        Assert.Throws<InvalidScriptException>(() => builder.workingRoot(path));
    }
}
