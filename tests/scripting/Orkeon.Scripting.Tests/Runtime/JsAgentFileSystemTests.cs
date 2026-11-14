using Jint;
using Orkeon.Application.Agent;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>#378: an agent body reads, as <c>ctx.fileSystem.prompt</c>, the <c>## File system</c> section a declarative turn holds.</summary>
public sealed class JsAgentFileSystemTests
{
    private static readonly TimeSpan Safety = HangGuards.EnginePromise;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeFileSystemService _fileSystem = new FakeFileSystemService()
        .AddMount("/workspace", FileAccessRights.ReadWrite)
        .AddMount("/docs", FileAccessRights.ReadOnly);

    private Engine NewEngine(ScriptingHostPorts? hostPorts = null) => new JsEngineFactory(
        builtInTools: [new StubBaseTool("file_read") { UsesFileSystem = true }, new StubBaseTool("web_search")],
        llmProvider: new FakeWriteOnceLlmProvider(),
        hostPorts: hostPorts ?? new ScriptingHostPorts { FileSystem = _fileSystem }).Create();

    /// <summary>Builds agent <c>a</c> with <paramref name="agentCalls"/>, its body returning <c>ctx.fileSystem.prompt</c>, in <c>globalThis.crew</c>.</summary>
    private static async Task DefineCrewAsync(Engine engine, string agentCalls)
        => await engine.EvaluateAsync($$"""
            const a = agentBuilder().name("a").role("R").goal("G"){{agentCalls}}
                .body(async (input, ctx) => ctx.fileSystem.prompt)
                .build();
            globalThis.crew = crewBuilder().name("c").withAgent(a).build();
            """, null, Ct);

    private static async Task<string> RunAgentAsync(Engine engine)
    {
        var outcome = await engine.EvaluateAsync("""crew.runAgent("a", "in").then(String)""", null, Ct)
            .WaitAsync(Safety, Ct);
        return outcome.AsString();
    }

    /// <summary>Runs agent <c>a</c>, built with <paramref name="agentCalls"/>, whose body returns <c>ctx.fileSystem.prompt</c>.</summary>
    private async Task<string> PromptOfBodyAsync(string agentCalls, ScriptingHostPorts? hostPorts = null)
    {
        var engine = NewEngine(hostPorts);
        await DefineCrewAsync(engine, agentCalls);
        return await RunAgentAsync(engine);
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldGiveDeclarativeSection_WhenBodyAgentHasFileSystemTool()
    {
        _fileSystem.WorkingDirectory = "/workspace";

        var prompt = await PromptOfBodyAsync(""".tools(["file_read"])""");

        Assert.Equal(AgentFileSystemPrompt.Render(_fileSystem.GetAvailableMounts(), null, "/workspace", isRoot: false), prompt);
        Assert.StartsWith("## File system", prompt, StringComparison.Ordinal);
        var docs = prompt.IndexOf("- /docs (read-only)", StringComparison.Ordinal);
        var workspace = prompt.IndexOf("- /workspace (read-write)", StringComparison.Ordinal);
        Assert.True(docs >= 0 && workspace > docs, prompt);
        Assert.EndsWith("Write intermediate notes under a read-write path.", prompt, StringComparison.Ordinal);
        Assert.Contains("Relative paths resolve against /workspace.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldListScopeAndRoot_WhenBodyAgentIsScopedAndRooted()
    {
        // Arrange: the scope the runtime resolves for this agent (AgentWorkingRoot.ResolveScope, no parent).
        _fileSystem.WorkingDirectory = "/workspace";
        var mounts = _fileSystem.GetAvailableMounts();
        var scope = AgentWorkingRoot.ResolveScope("a",
            [new FileSystemScopeEntry("/workspace/src"), new FileSystemScopeEntry("/docs", FileAccessRights.ReadOnly)],
            mounts, parent: null, root: "/workspace/src");

        // Act
        var prompt = await PromptOfBodyAsync(
            """.tools(["file_read"]).workingRoot("/workspace/src").filesystem(["/workspace/src", { path: "/docs", access: "ro" }])""");

        // Assert
        Assert.Equal(AgentFileSystemPrompt.Render(mounts, scope, "/workspace/src", isRoot: true), prompt);
        Assert.Contains("- /docs (read-only)", prompt, StringComparison.Ordinal);
        Assert.Contains("- /workspace/src (read-write)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("- /workspace (read-write)", prompt, StringComparison.Ordinal);
        Assert.Contains("Your working root is /workspace/src: relative paths resolve there.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldSayOutsideScope_WhenBodyAgentIsScopedWithoutRoot()
    {
        _fileSystem.WorkingDirectory = "/workspace";

        var prompt = await PromptOfBodyAsync(""".tools(["file_read"]).filesystem(["/workspace/src"])""");

        Assert.Contains(
            "Relative paths resolve against /workspace, outside your file-system scope: write every path in full, under one of the paths above.",
            prompt, StringComparison.Ordinal);
        Assert.Contains("- /workspace/src (read-write)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldSayPlainResolution_WhenBodyAgentScopeCoversWorkingDirectory()
    {
        _fileSystem.WorkingDirectory = "/workspace/src";

        var prompt = await PromptOfBodyAsync(""".tools(["file_read"]).filesystem(["/workspace"])""");

        Assert.Contains("Relative paths resolve against /workspace/src.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("outside your file-system scope", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldReflectNewMount_WhenMountIsAddedBetweenRuns()
    {
        // Arrange
        var engine = NewEngine();
        await DefineCrewAsync(engine, """.tools(["file_read"])""");
        var before = await RunAgentAsync(engine);

        // Act
        _fileSystem.AddMount("/out", FileAccessRights.ReadWrite);
        var after = await RunAgentAsync(engine);

        // Assert
        Assert.DoesNotContain("- /out (read-write)", before, StringComparison.Ordinal);
        Assert.Contains("- /out (read-write)", after, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldBeEmpty_WhenHostHasNoFileSystem()
        => Assert.Equal("", await PromptOfBodyAsync(""".tools(["file_read"])""", new ScriptingHostPorts()));

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldBeEmpty_WhenAgentHasOnlyAutonomousTool()
    {
        // A toolBuilder() tool does not declare file-system use: the agent has no file-system tool.
        var engine = NewEngine();
        await engine.EvaluateAsync("""
            const own = toolBuilder().name("own").description("d").execute(() => "x").build();
            const a = agentBuilder().name("a").role("R").goal("G").withAutonomousTool(own)
                .body(async (input, ctx) => ctx.fileSystem.prompt)
                .build();
            globalThis.crew = crewBuilder().name("c").withAgent(a).build();
            """, null, Ct);

        Assert.Equal("", await RunAgentAsync(engine));
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldGiveParentRoot_WhenChildBodyReadsPrompt()
    {
        // Arrange: a spawned child with no root of its own runs under its parent's (RT-06).
        var engine = NewEngine();
        await engine.EvaluateAsync("""
            const kid = agentBuilder().name("kid").role("R").goal("G").tools(["file_read"])
                .body(async (input, ctx) => { globalThis.kidPrompt = ctx.fileSystem.prompt; return "kid"; }).build();
            const root = agentBuilder().name("root").role("R").goal("G").workingRoot("/workspace/src")
                .body(async (input, ctx) => {
                    ctx.spawnBackground("kid", "x");
                    return (await ctx.llm.act("wait", { permissionMode: "bypassPermissions", waitFor: "children", maxIterations: 5 })).output;
                }).build();
            globalThis.crew = crewBuilder().name("c").withAgent(root).withAgent(kid).build();
            """, null, Ct);

        // Act
        await engine.EvaluateAsync("""crew.runAgent("root", "in").then(String)""", null, Ct).WaitAsync(Safety, Ct);
        var prompt = engine.GetValue("kidPrompt");

        // Assert
        Assert.True(prompt.IsString(), "the child body never ran");
        Assert.Contains("Your working root is /workspace/src: relative paths resolve there.", prompt.AsString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldBeEmpty_WhenBodyAgentOptsOut()
        => Assert.Equal("", await PromptOfBodyAsync(""".tools(["file_read"]).announceMounts(false)"""));

    [Fact]
    [Trait("BR", "#378")]
    public async Task ShouldBeEmpty_WhenBodyAgentHasNoFileSystemTool()
        => Assert.Equal("", await PromptOfBodyAsync(""".tools(["web_search"])"""));
}
