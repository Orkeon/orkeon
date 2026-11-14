using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.Doubles;
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
        => new AgentBuilder().Role("Writer").Goal("Write").WithTool(new StubBaseTool(fileTool ? "file_read" : "web_search") { UsesFileSystem = fileTool }).AnnounceMounts(announce).Build();

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
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldSayWorkingDirectoryIsOutsideScope_WhenScopedRunHasNoRoot()
    {
        _fileSystem.WorkingDirectory = "/workspace";
        var scope = AgentFileSystemScope.Resolve(
            "Writer", [new FileSystemScopeEntry("/workspace/project")], _fileSystem.GetAvailableMounts());
        await using var run = AgentRunScope.Begin("id", "Writer", null, scope);

        var prompt = Prompt(Agent());

        Assert.DoesNotContain("Relative paths resolve against /workspace.", prompt, StringComparison.Ordinal);
        Assert.Contains(
            "Relative paths resolve against /workspace, outside your file-system scope: write every path in full, under one of the paths above.",
            prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#348")]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldNameWorkingDirectory_WhenScopeCoversIt()
    {
        _fileSystem.WorkingDirectory = "/workspace";
        var scope = AgentFileSystemScope.Resolve(
            "Writer", [new FileSystemScopeEntry("/workspace", FileAccessRights.ReadOnly)], _fileSystem.GetAvailableMounts());
        await using var run = AgentRunScope.Begin("id", "Writer", null, scope);

        Assert.Contains("Relative paths resolve against /workspace.", Prompt(Agent()), StringComparison.Ordinal);
    }

    private static string CrewYaml(string crewLine) => $$"""
        name: announced
        goal: Read
        process: sequential
        {{crewLine}}
        agents:
          reader:
            role: Reader
            goal: Read the docs
            tools: [file_read]
        tasks:
          read:
            description: Read the docs
            expected_output: A summary
            agent: reader
        """;

    private async System.Threading.Tasks.Task<string> PromptOfLoadedAgentAsync(string yaml)
    {
        var ct = TestContext.Current.CancellationToken;
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var registry = new TestToolRegistry();
        await registry.RegisterToolAsync(new StubBaseTool("file_read") { UsesFileSystem = true });
        var agents = new TestAgentRepository();
        var factory = new CrewFactory(
            loader, registry, NullLogger<CrewFactory>.Instance, new TestCrewRepository(), agents, new TestTaskRepository());
        var crew = await factory.CreateFromConfigAsync(await loader.LoadFromStringAsync(yaml, ct), ct);
        return Prompt((await agents.GetByIdAsync(crew.Agents.Single(), ct))!);
    }

    [Theory]
    [Trait("BR", "#348")]
    [InlineData("", true)]
    [InlineData("announce_mounts: false", false)]
    public async System.Threading.Tasks.Task ShouldFollowCrewSetting_WhenAgentIsBuiltByCrewFactory(string crewLine, bool announced)
    {
        var prompt = await PromptOfLoadedAgentAsync(CrewYaml(crewLine));

        Assert.Equal(announced, prompt.Contains("## File system", StringComparison.Ordinal));
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
}
