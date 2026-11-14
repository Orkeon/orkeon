using Jint;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>#349: a script agent's <c>.filesystem([...])</c> holds its tools as YAML's <c>filesystem:</c> holds a declared agent's.</summary>
public sealed class ScriptAgentFileSystemScopeTests
{
    private static readonly TimeSpan Safety = HangGuards.EnginePromise;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeRightsFileSystemService NewFileSystem() => new FakeRightsFileSystemService()
        .AddMount("/workspace", FileAccessRights.ReadWrite)
        .AddMount("/docs", FileAccessRights.ReadOnly);

    private static Jint.Engine NewEngine(FakeRunScopeProbeTool probe, ILlmProvider? llm = null, RagScriptingBackend? rag = null, FakeRightsFileSystemService? fileSystem = null)
        => new JsEngineFactory(
            builtInTools: [probe],
            llmProvider: llm ?? new FakeWriteOnceLlmProvider(),
            hostPorts: new ScriptingHostPorts { FileSystem = fileSystem ?? NewFileSystem() },
            ragBackend: rag).Create();

    private static async Task<JsCrew> BuildCrewAsync(Jint.Engine engine, string js)
        => (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;

    /// <summary>Evaluates <paramref name="js"/> (which sets <c>globalThis.crew</c>), then runs <paramref name="agent"/> alone: its output, or <c>error: </c> and the message.</summary>
    private static async Task<string> RunAgentAsync(Jint.Engine engine, string js, string agent)
    {
        await engine.EvaluateAsync(js, null, Ct);
        var outcome = await engine.EvaluateAsync($$"""
            (async () => {
                try { return String(await crew.runAgent("{{agent}}", "in")); }
                catch (e) { return "error: " + String(e?.message ?? e); }
            })()
            """, null, Ct);
        return outcome.AsString();
    }

    private static string CrewOf(string scopeCall) => $$"""
        const a = agentBuilder().name("a").role("R").goal("G").tools(["file_write"]){{scopeCall}}
            .body(async (input, ctx) => (await ctx.llm.act("go", { permissionMode: "bypassPermissions" })).output)
            .build();
        crewBuilder().name("c").withAgent(a).build();
        """;

    private static string MessagesOf(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            messages.Add(e.Message);
        return string.Join(" | ", messages);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRunActToolsUnderScope_WhenAgentDeclaresFilesystem()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe),
            CrewOf(""".filesystem(["/workspace/src", { path: "/docs", access: "ro" }])"""));

        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);

        Assert.Equal("/docs (ro), /workspace/src", Assert.Single(probe.Scopes));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseRunAtStart_WhenEntryIsUnderNoMount()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), CrewOf(""".filesystem(["/nowhere"])"""));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, Ct).WaitAsync(Safety, Ct));

        Assert.Contains("The file-system scope of agent 'a': '/nowhere' is not under any mount", MessagesOf(ex), StringComparison.Ordinal);
        Assert.Empty(probe.Scopes);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseRunAtStart_WhenAccessExceedsMount()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), CrewOf(""".filesystem([{ path: "/docs", access: "rw" }])"""));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, Ct).WaitAsync(Safety, Ct));

        Assert.Contains("'/docs' asks rw but the mount grants ro", MessagesOf(ex), StringComparison.Ordinal);
        Assert.Empty(probe.Scopes);
    }

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("[]", "non-empty array")]
    [InlineData("""[{ path: "/workspace", mode: "ro" }]""", "unknown key 'mode'")]
    [InlineData("""[{ path: "/workspace", access: "all" }]""", "ro, rw, rwnd")]
    [InlineData("[42]", "an entry is a path")]
    public async Task ShouldRefuseAtBuild_WhenEntriesAreMalformed(string entries, string expected)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => BuildCrewAsync(NewEngine(new FakeRunScopeProbeTool()), CrewOf($".filesystem({entries})")));

        Assert.Contains(expected, MessagesOf(ex), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRunChildUnderParentScope_WhenSpawnedChildDeclaresNone()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), """
            const kid = agentBuilder().name("kid").role("R").goal("G")
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "kid"; }).build();
            const own = agentBuilder().name("own").role("R").goal("G").filesystem(["/workspace/src/lib"])
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "own"; }).build();
            const root = agentBuilder().name("root").role("R").goal("G").filesystem(["/workspace/src"])
                .body(async (input, ctx) => {
                    ctx.spawnBackground("kid", "x");
                    ctx.spawnBackground("own", "y");
                    return (await ctx.llm.act("wait", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 5 })).output;
                }).build();
            crewBuilder().name("c").withAgent(root).withAgent(kid).withAgent(own).build();
            """);

        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);

        // The run also steps through kid (no scope: no parent) and own on their own; spawned, kid runs
        // under its parent's scope and own under its own, which narrows it.
        Assert.Equal([null, "/workspace/src", "/workspace/src/lib", "/workspace/src/lib"], probe.Scopes.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseSpawnedChildAtStart_WhenItsEntryLiesOutsideParentScope()
    {
        var probe = new FakeRunScopeProbeTool();
        var engine = NewEngine(probe, new FakeToolCallingLlmProvider("spawn_agent", """{"agent":"own","input":"y"}"""));

        var outcome = await RunAgentAsync(engine, """
            const own = agentBuilder().name("own").role("R").goal("G").filesystem(["/docs"])
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "own"; }).build();
            const root = agentBuilder().name("root").role("R").goal("G").filesystem(["/workspace/src"])
                .body(async (input, ctx) => (await ctx.llm.act("go", { permissionMode: "bypassPermissions" })).output).build();
            globalThis.crew = crewBuilder().name("c").withAgent(root).withAgent(own).build();
            """, "root");

        Assert.Contains("'/docs' lies outside the file-system scope of 'root' that started it", outcome, StringComparison.Ordinal);
        Assert.Empty(probe.Scopes);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldSendUnderParentScope_WhenInstanceWasSpawnedFromScopedAgent()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), """
            const kid = agentBuilder().name("kid").role("R").goal("G").tools(["file_write"])
                .body(async () => "kid").build();
            const root = agentBuilder().name("root").role("R").goal("G").filesystem(["/workspace/src"])
                .body(async (input, ctx) => {
                    ctx.spawnBackground("kid", "x");
                    await ctx.llm.act("wait", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 5 });
                    return await c.send("a1", "more");
                }).build();
            const c = crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            c;
            """);

        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);

        // Only the send's bare act calls the tool: it runs under the scope the spawn handed the instance.
        Assert.Equal("/workspace/src", Assert.Single(probe.Scopes));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldBoundDelegatedRun_WhenScopedBodyDelegatesToUnscopedAgent()
    {
        var probe = new FakeRunScopeProbeTool();

        var outcome = await RunAgentAsync(NewEngine(probe), """
            const kid = agentBuilder().name("kid").role("R").goal("G")
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "kid"; }).build();
            const root = agentBuilder().name("root").role("R").goal("G").filesystem(["/workspace/src"]).workingRoot("/workspace/src")
                .body(async (input, ctx) => await ctx.delegate("kid", "x")).build();
            globalThis.crew = crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            """, "root");

        Assert.Equal("kid", outcome);
        Assert.Equal("/workspace/src", Assert.Single(probe.Scopes));
        // A delegated run keeps its own working root, as delegate_work does: only the scope is handed down.
        Assert.Null(Assert.Single(probe.Roots));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseDelegatedRun_WhenChildEntryLiesOutsideCallerScope()
    {
        var probe = new FakeRunScopeProbeTool();

        var outcome = await RunAgentAsync(NewEngine(probe), """
            const kid = agentBuilder().name("kid").role("R").goal("G").filesystem(["/docs"])
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "kid"; }).build();
            const root = agentBuilder().name("root").role("R").goal("G").filesystem(["/workspace/src"])
                .body(async (input, ctx) => await ctx.delegate("kid", "x")).build();
            globalThis.crew = crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            """, "root");

        Assert.Contains("'/docs' lies outside the file-system scope of 'root' that started it", outcome, StringComparison.Ordinal);
        Assert.Empty(probe.Scopes);
    }

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("""crew.runAgent("kid", "x")""")]
    [InlineData("""sub.runAgent("solo", "x")""")]
    [InlineData("sub.run()")]
    public async Task ShouldBoundRunStartedFromCode_WhenScopedBodyStartsIt(string call)
    {
        var probe = new FakeRunScopeProbeTool();

        var outcome = await RunAgentAsync(NewEngine(probe), $$"""
            const kid = agentBuilder().name("kid").role("R").goal("G")
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "kid"; }).build();
            const solo = agentBuilder().name("solo").role("R").goal("G")
                .body(async (input, ctx) => { await ctx.tools.fileWrite({}); return "solo"; }).build();
            globalThis.sub = crewBuilder().name("s").withAgent(solo).build();
            const root = agentBuilder().name("root").role("R").goal("G").filesystem(["/workspace/src"])
                .body(async () => { await {{call}}; return "root"; }).build();
            globalThis.crew = crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            """, "root");

        Assert.Equal("root", outcome);
        Assert.Equal("/workspace/src", Assert.Single(probe.Scopes));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRunLifecycleHookToolsUnderAgentScope_WhenHookActs()
    {
        var probe = new FakeRunScopeProbeTool();
        var engine = NewEngine(probe);
        await engine.EvaluateAsync("""
            const a = agentBuilder().name("a").role("R").goal("G").tools(["file_write"]).filesystem(["/workspace/src"])
                .onAgentStart(ctx => { globalThis.hook = ctx.llm.act("go", { permissionMode: "bypassPermissions" }); return globalThis.hook; })
                .build();
            crewBuilder().name("c").withAgent(a).build();
            """, null, Ct);

        var output = await engine.EvaluateAsync("(async () => (await globalThis.hook).output)()", null, Ct);

        Assert.Equal("final", output.AsString());
        Assert.Equal("/workspace/src", Assert.Single(probe.Scopes));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldSkipLifecycleHook_WhenAgentScopeIsRefused()
    {
        var engine = NewEngine(new FakeRunScopeProbeTool());

        var ran = await engine.EvaluateAsync("""
            const a = agentBuilder().name("a").role("R").goal("G").filesystem(["/nowhere"])
                .onAgentStart(() => { globalThis.ran = true; }).build();
            crewBuilder().name("c").withAgent(a).build();
            globalThis.ran === true;
            """, null, Ct);

        // The crew still builds: the scope is refused when the agent's run starts, and the hook, which
        // could not be held to it, does not run.
        Assert.False(ran.AsBoolean());
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRunCrewHookToolsUnderAnchorAgentScope_WhenCrewHookActs()
    {
        var probe = new FakeRunScopeProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), """
            const a = agentBuilder().name("a").role("R").goal("G").tools(["file_write"]).filesystem(["/workspace/src"])
                .body(async () => "a").build();
            crewBuilder().name("c").withAgent(a)
                .onCrewStart(async ctx => { await ctx.llm.act("go", { permissionMode: "bypassPermissions" }); })
                .build();
            """);

        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);

        Assert.Equal("/workspace/src", Assert.Single(probe.Scopes));
    }

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("/docs/a.md", "error: ", 0)]
    [InlineData("/workspace/src/a.md", "k", 1)]
    public async Task ShouldHoldRagIngestToAgentScope_WhenScopedBodyCallsIt(string source, string expected, int ingested)
    {
        var fileSystem = NewFileSystem();
        var pipeline = new FakeIngestionPipeline();
        var rag = new RagScriptingBackend { IngestionPipeline = pipeline, RagPipeline = new FakeRagPipeline(), FileSystem = fileSystem };

        var outcome = await RunAgentAsync(NewEngine(new FakeRunScopeProbeTool(), rag: rag, fileSystem: fileSystem), $$"""
            const a = agentBuilder().name("a").role("R").goal("G").filesystem(["/workspace/src"])
                .body(async () => (await rag.ingest({ collection: "k", sources: ["{{source}}"] })).collection).build();
            globalThis.crew = crewBuilder().name("c").withAgent(a).build();
            """, "a");

        Assert.StartsWith(expected, outcome, StringComparison.Ordinal);
        if (ingested == 0)
            Assert.Contains("Access denied for '/docs/a.md': the path is outside the file-system scope of agent 'a'", outcome, StringComparison.Ordinal);
        Assert.Equal(ingested, pipeline.CallCount);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseGlobalToolCall_WhenOpenScopesDescribeAlikeButGrantDifferentRights()
    {
        var engine = NewEngine(new FakeRunScopeProbeTool());
        var crew = await BuildCrewAsync(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").build();
            crewBuilder().name("c").withAgent(a).build();
            """);
        var mounts = NewFileSystem().GetAvailableMounts();
        var parent = AgentFileSystemScope.Resolve("p", [new FileSystemScopeEntry("/workspace/src", FileAccessRights.ReadOnly)], mounts);
        var narrowed = AgentFileSystemScope.Resolve("k", [new FileSystemScopeEntry("/workspace/src")], mounts, parent);
        var wide = AgentFileSystemScope.Resolve("q", [new FileSystemScopeEntry("/workspace/src")], mounts);
        using var run = new CrewRunScope(crew, CrewRunKind.Agent, null, null, ownedByHost: false, CancellationToken.None);
        await using var narrowedRun = AgentRunScope.Create("k", "k", null, narrowed);
        await using var wideRun = AgentRunScope.Create("q", "q", null, wide);
        var k = new BrokerAttribution("k", run) { RunScope = narrowedRun };
        var q = new BrokerAttribution("q", run) { RunScope = wideRun };
        JsEngineAttempts.Open(engine, k);
        JsEngineAttempts.Open(engine, q);
        try
        {
            Assert.Equal(narrowed.Describe(), wide.Describe());

            var ex = Assert.Throws<InvalidOperationException>(() => JsEngineAttempts.ScopeForCode(engine, "fileWrite"));

            Assert.Contains("agents with different working roots run (or different file-system scopes)", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            JsEngineAttempts.Close(engine, q);
            JsEngineAttempts.Close(engine, k);
        }
    }

    [Fact]
    [Trait("BR", "#349")]
    [Trait("BR", "#348")]
    public async Task ShouldMapScopeAndAnnounce_WhenCrewRunsOnDeclarativeShape()
    {
        var crew = await BuildCrewAsync(NewEngine(new FakeRunScopeProbeTool()), """
            const a = agentBuilder().name("a").role("R").goal("G").filesystem([{ path: "/docs", access: "ro" }]).announceMounts(false).build();
            const b = agentBuilder().name("b").role("R").goal("G").build();
            crewBuilder().name("c").withAgent(a).withAgent(b).build();
            """);

        var agents = JsCrewConfigurationAdapter.ToConfiguration(crew).Agents;

        Assert.Equal(new FileSystemScopeEntry("/docs", FileAccessRights.ReadOnly), Assert.Single(agents[0].FileSystem));
        Assert.False(agents[0].AnnounceMounts);
        Assert.Empty(agents[1].FileSystem);
        Assert.True(agents[1].AnnounceMounts);
    }
}
