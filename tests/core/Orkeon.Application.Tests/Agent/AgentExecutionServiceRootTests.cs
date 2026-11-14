using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Agent;
using Orkeon.Application.Context;
using Orkeon.Application.Tests.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Agent;

/// <summary>RT-06: an agent's virtual working root reaches its run scope, and an unmounted one is refused.</summary>
public sealed class AgentExecutionServiceRootTests : IDisposable
{
    private readonly TestExecutionOrchestrator _orchestrator = new();
    private readonly TestMemoryScope _memoryScope = new();
    private readonly FakeRightsFileSystemService _fileSystem = new FakeRightsFileSystemService()
        .AddMount("/workspace", FileAccessRights.Read)
        .AddMount("/output", FileAccessRights.Read);

    public void Dispose() => _memoryScope.Dispose();

    private AgentExecutionService CreateService() => new(
        NullLogger<AgentExecutionService>.Instance,
        _orchestrator,
        new TestCallbackOrchestrator(),
        new TestMemoryCoordinator(),
        fileSystem: _fileSystem);

    private SimpleExecutionContext CreateContext() => new(
        CrewId: CrewId.From(Guid.NewGuid()),
        Variables: new Dictionary<string, string>(),
        Memory: _memoryScope,
        PreviousOutputs: [],
        CancellationToken: CancellationToken.None);

    private static DomainAgent CreateAgent(string root) => new AgentBuilder()
        .Role("Rooted")
        .Goal("Work under a root")
        .WorkingRoot(root)
        .Build();

    private static CrewTask CreateTask() => CrewTask.Create(
        TaskDescription.From("Test task"),
        ExpectedOutput.From("Expected output"));

    [Fact]
    [Trait("BR", "RT-06")]
    public async System.Threading.Tasks.Task ShouldCarryWorkingRootToRunScope_WhenYamlDeclaresIt()
    {
        var mapper = new Orkeon.Infrastructure.Configuration.YamlCrewMapper(NullLogger.Instance);
        var configuration = mapper.BuildConfiguration(
            new Orkeon.Infrastructure.Configuration.CrewMappingSettings { Name = "crew", Process = "sequential" },
            new Dictionary<string, Orkeon.Infrastructure.Configuration.AgentYamlConfig>
            {
                ["rooted"] = new() { Role = "Rooted", Goal = "Work under a root", WorkingRoot = "/workspace/project" },
            },
            null);
        var mappedRoot = Assert.Single(configuration.Agents).WorkingRoot;
        Assert.Equal("/workspace/project", mappedRoot);
        var agent = CreateAgent(mappedRoot!);

        await CreateService().ExecuteTaskAsync(agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(_orchestrator.RunScopeDuringRun);
        Assert.Equal("/workspace/project", _orchestrator.RunScopeDuringRun!.Root);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async System.Threading.Tasks.Task ShouldRefuseRootOutsideMounts_WhenAgentRootNotMounted()
    {
        var agent = CreateAgent("/nowhere/x");

        string refusal;
        try
        {
            var result = await CreateService().ExecuteTaskAsync(agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);
            Assert.False(result.Success);
            refusal = $"{result.Error}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            refusal = ex.Message;
        }

        Assert.Contains("/nowhere/x", refusal);
        Assert.Null(_orchestrator.RunScopeDuringRun);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async System.Threading.Tasks.Task ShouldRefuseRoot_WhenRootHoldsParentSegments()
    {
        var agent = CreateAgent("/workspace/../nowhere");

        string refusal;
        try
        {
            var result = await CreateService().ExecuteTaskAsync(agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);
            Assert.False(result.Success);
            refusal = $"{result.Error}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            refusal = ex.Message;
        }

        Assert.Contains("/workspace/../nowhere", refusal);
        Assert.Null(_orchestrator.RunScopeDuringRun);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async System.Threading.Tasks.Task ShouldAcceptRoot_WhenRootHasRedundantSeparators()
    {
        var agent = CreateAgent("/workspace//project/");

        await CreateService().ExecuteTaskAsync(agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(_orchestrator.RunScopeDuringRun);
        Assert.Equal("/workspace/project", _orchestrator.RunScopeDuringRun!.Root);
    }

    private async System.Threading.Tasks.Task<string> RefusalAsync(DomainAgent agent)
    {
        try
        {
            var result = await CreateService().ExecuteTaskAsync(agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);
            Assert.False(result.Success);
            return $"{result.Error}";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    [Fact]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldCarryFileSystemScopeToRunScope_WhenAgentDeclaresOne()
    {
        var agent = new AgentBuilder().Role("Scoped").Goal("Read docs")
            .FileSystem(new FileSystemScopeEntry("/workspace/docs"))
            .Build();

        await CreateService().ExecuteTaskAsync(agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        var scope = _orchestrator.RunScopeDuringRun?.FileSystem;
        Assert.NotNull(scope);
        Assert.Equal("Scoped", scope.AgentName);
        Assert.True(scope.Allows("/workspace/docs/a.md", FileAccessRights.Read));
        Assert.False(scope.Allows("/workspace/other/a.md", FileAccessRights.Read));
        Assert.False(scope.Allows("/output/a.md", FileAccessRights.Read));
    }

    [Fact]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldRefuseRun_WhenScopeEntryIsUnderNoMount()
    {
        var agent = new AgentBuilder().Role("Scoped").Goal("Read docs")
            .FileSystem(new FileSystemScopeEntry("/nowhere/docs"))
            .Build();

        var refusal = await RefusalAsync(agent);

        Assert.Contains("'Scoped'", refusal, StringComparison.Ordinal);
        Assert.Contains("'/nowhere/docs'", refusal, StringComparison.Ordinal);
        Assert.Null(_orchestrator.RunScopeDuringRun);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldRefuseRun_WhenScopeAccessExceedsMount()
    {
        var agent = new AgentBuilder().Role("Scoped").Goal("Write docs")
            .FileSystem(new FileSystemScopeEntry("/workspace/docs", FileAccessRights.ReadWrite))
            .Build();

        var refusal = await RefusalAsync(agent);

        Assert.Contains("'/workspace/docs'", refusal, StringComparison.Ordinal);
        Assert.Null(_orchestrator.RunScopeDuringRun);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldRefuseRun_WhenWorkingRootLiesOutsideScope()
    {
        var agent = new AgentBuilder().Role("Scoped").Goal("Read docs")
            .WorkingRoot("/output")
            .FileSystem(new FileSystemScopeEntry("/workspace/docs"))
            .Build();

        var refusal = await RefusalAsync(agent);

        Assert.Equal(
            "The agent working root '/output' of agent 'Scoped' lies outside its file-system scope: /workspace/docs.",
            refusal);
        Assert.Null(_orchestrator.RunScopeDuringRun);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async System.Threading.Tasks.Task ShouldBoundDelegatedRun_WhenStartedFromScopedToolCall()
    {
        var parent = AgentFileSystemScope.Resolve("Parent", [new FileSystemScopeEntry("/workspace/docs")], _fileSystem.GetAvailableMounts());
        await using var run = Orkeon.Domain.Execution.AgentRunScope.Begin("p", "Parent", null, parent);
        using var call = Orkeon.Domain.Execution.AgentRunScope.EnterToolCall();
        var child = new AgentBuilder().Role("Child").Goal("Read").Build();

        await CreateService().ExecuteTaskAsync(child, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        var scope = _orchestrator.RunScopeDuringRun?.FileSystem;
        Assert.NotNull(scope);
        Assert.Equal("Child", scope.AgentName);
        Assert.Same(parent, scope.Parent);
        Assert.Equal(parent.Entries, scope.Entries);
        Assert.Equal(FileAccessRights.Read, scope.RightsAt("/workspace/docs/a.md") & FileAccessRights.Read);
        Assert.Equal(FileAccessRights.None, scope.RightsAt("/output/a.md"));
        Assert.Contains("of agent 'Child'", scope.DescribeDenial("/output/a.md", FileAccessRights.Read), StringComparison.Ordinal);
    }
}
