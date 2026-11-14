using System.Collections.Concurrent;
using Jint;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>#349: a script agent's <c>.filesystem([...])</c> holds its tools as YAML's <c>filesystem:</c> holds a declared agent's.</summary>
public sealed class ScriptAgentFileSystemScopeTests
{
    private static readonly TimeSpan Safety = HangGuards.EnginePromise;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Records the file-system scope of the run each call runs in, as its description (<c>null</c> without one).</summary>
    private sealed class ProbeTool : IBaseTool
    {
        public ConcurrentQueue<string?> Scopes { get; } = new();

        public string Name => "file_write";
        public ToolAccess Access => ToolAccess.Read;
        public string Description => "probe";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public Task<ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
        {
            Scopes.Enqueue(AgentRunScope.Current?.FileSystem?.Describe());
            return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    /// <summary>The first turn of each act asks for one <c>file_write</c> call; the turn after it answers.</summary>
    private sealed class FakeWriteOnceLlmProvider : ILlmProvider
    {
        public string Name => "fake-write-once";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = prompt });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            if (messages.Any(m => m.Role == "tool") || config?.Tools is null)
                return Task.FromResult(new LlmResponse { Content = "final" });
            return Task.FromResult(new LlmResponse
            {
                Content = "",
                RawResponseBody = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
                    "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"file_write\",\"arguments\":\"{}\"}}]}}]}",
            });
        }
    }

    private static Jint.Engine NewEngine(ProbeTool probe)
    {
        var fileSystem = new FakeRightsFileSystemService()
            .AddMount("/workspace", FileAccessRights.ReadWrite)
            .AddMount("/docs", FileAccessRights.ReadOnly);
        return new JsEngineFactory(
            builtInTools: [probe],
            llmProvider: new FakeWriteOnceLlmProvider(),
            hostPorts: new ScriptingHostPorts { FileSystem = fileSystem }).Create();
    }

    private static async Task<JsCrew> BuildCrewAsync(Jint.Engine engine, string js)
        => (JsCrew)(await engine.EvaluateAsync(js, null, Ct)).ToObject()!;

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
        var probe = new ProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe),
            CrewOf(""".filesystem(["/workspace/src", { path: "/docs", access: "ro" }])"""));

        await crew.RunAsync(null, Ct).WaitAsync(Safety, Ct);

        Assert.Equal("/docs (ro), /workspace/src", Assert.Single(probe.Scopes));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseRunAtStart_WhenEntryIsUnderNoMount()
    {
        var probe = new ProbeTool();
        var crew = await BuildCrewAsync(NewEngine(probe), CrewOf(""".filesystem(["/nowhere"])"""));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, Ct).WaitAsync(Safety, Ct));

        Assert.Contains("The file-system scope of agent 'a': '/nowhere' is not under any mount", MessagesOf(ex), StringComparison.Ordinal);
        Assert.Empty(probe.Scopes);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRefuseRunAtStart_WhenAccessExceedsMount()
    {
        var probe = new ProbeTool();
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
            () => BuildCrewAsync(NewEngine(new ProbeTool()), CrewOf($".filesystem({entries})")));

        Assert.Contains(expected, MessagesOf(ex), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldRunChildUnderParentScope_WhenSpawnedChildDeclaresNone()
    {
        var probe = new ProbeTool();
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
    [Trait("BR", "#348")]
    public async Task ShouldMapScopeAndAnnounce_WhenCrewRunsOnDeclarativeShape()
    {
        var crew = await BuildCrewAsync(NewEngine(new ProbeTool()), """
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
