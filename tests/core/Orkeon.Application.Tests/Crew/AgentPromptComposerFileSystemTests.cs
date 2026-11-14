using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew;

/// <summary>#348: the system prompt names the virtual paths an agent with a file-system tool may touch.</summary>
public sealed class AgentPromptComposerFileSystemTests
{
    private readonly FakeFileSystemService _fileSystem = new FakeFileSystemService()
        .AddMount("/workspace", FileAccessRights.ReadWrite)
        .AddMount("/docs", FileAccessRights.ReadOnly);

    private static DomainAgent Agent(bool fileTool = true, bool announce = true)
        => new AgentBuilder().Role("Writer").Goal("Write").WithTool(new StubTool(fileTool)).AnnounceMounts(announce).Build();

    private string Prompt(DomainAgent agent)
    {
        var task = new CrewTaskBuilder().Description("Do work").ExpectedOutput("Result").Build();
        return AgentPromptComposer.BuildSystemPrompt(agent, task, TaskToolbelt.Compose(agent, task), supportsNativeToolCalling: true, _fileSystem);
    }

    [Fact]
    [Trait("BR", "#348")]
    public void ShouldListMountsSorted_WhenAgentHasFileSystemTool()
    {
        _fileSystem.WorkingDirectory = "/workspace";

        var prompt = Prompt(Agent());

        Assert.Contains("## File system", prompt, StringComparison.Ordinal);
        var docs = prompt.IndexOf("- /docs (read-only)", StringComparison.Ordinal);
        var workspace = prompt.IndexOf("- /workspace (read-write)", StringComparison.Ordinal);
        Assert.True(docs >= 0 && workspace > docs, prompt);
        Assert.Contains("Relative paths resolve against /workspace.", prompt, StringComparison.Ordinal);
        Assert.Contains("Write intermediate notes under a read-write path.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#348")]
    public void ShouldLeaveSectionOut_WhenAgentHasNoFileSystemTool()
        => Assert.DoesNotContain("## File system", Prompt(Agent(fileTool: false)), StringComparison.Ordinal);

    [Fact]
    [Trait("BR", "#348")]
    public void ShouldLeaveSectionOut_WhenAgentOptsOut()
        => Assert.DoesNotContain("## File system", Prompt(Agent(announce: false)), StringComparison.Ordinal);

    [Fact]
    [Trait("BR", "#348")]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldListScopeAndRoot_WhenRunIsScopedAndRooted()
    {
        var scope = AgentFileSystemScope.Resolve(
            "Writer", [new FileSystemScopeEntry("/workspace/project")], _fileSystem.GetAvailableMounts());
        await using var run = AgentRunScope.Begin("id", "Writer", "/workspace/project", scope);

        var prompt = Prompt(Agent());

        Assert.Contains("- /workspace/project (read-write)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("- /docs", prompt, StringComparison.Ordinal);
        Assert.Contains("Your working root is /workspace/project: relative paths resolve there.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#348")]
    public void ShouldGiveSamePrompt_WhenMountsAreRegisteredInAnotherOrder()
    {
        var reversed = new FakeFileSystemService()
            .AddMount("/docs", FileAccessRights.ReadOnly)
            .AddMount("/workspace", FileAccessRights.ReadWrite);
        var agent = Agent();
        var task = new CrewTaskBuilder().Description("Do work").ExpectedOutput("Result").Build();

        Assert.Equal(
            Prompt(agent),
            AgentPromptComposer.BuildSystemPrompt(agent, task, TaskToolbelt.Compose(agent, task), supportsNativeToolCalling: true, reversed));
    }

    private sealed class StubTool(bool usesFileSystem) : IBaseTool
    {
        public string Name => usesFileSystem ? "file_read" : "web_search";
        public string Description => "stub";
        public bool UsesFileSystem => usesFileSystem;
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(
            Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, "ok", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
