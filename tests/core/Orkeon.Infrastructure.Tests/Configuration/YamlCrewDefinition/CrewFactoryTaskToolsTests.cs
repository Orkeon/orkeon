using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-07: a task's <c>tools:</c> used to be read, validated and dropped — the agent kept its own
/// belt only. They are now resolved like an agent's (same registry, same strict check) and travel
/// on the task, which adds them to its agent's belt for that task only.
/// </summary>
public class CrewFactoryTaskToolsTests
{
    private const string Yaml = """
name: report-crew
goal: Write a report
agents:
  writer: { role: Writer, goal: Write, tools: [file_read] }
tasks:
  report:
    agent: writer
    description: Write the report to /output/report.md
    expectedOutput: A report
    tools: [file_write]
  review:
    agent: writer
    description: Review the report
    expectedOutput: A review
""";

    private static (YamlCrewDefinitionLoader loader, CrewFactory factory, InMemoryTaskRepository tasks, InMemoryAgentRepository agents, MockToolRegistry registry)
        Build(bool strictTools)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var unitOfWork = new NullUnitOfWork();
        var tasks = new InMemoryTaskRepository(unitOfWork);
        var agents = new InMemoryAgentRepository(unitOfWork);
        var registry = new MockToolRegistry();
        var factory = new CrewFactory(
            loader,
            registry,
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            agents,
            tasks,
            Options.Create(new CrewFactoryOptions { StrictTools = strictTools }));
        return (loader, factory, tasks, agents, registry);
    }

    [Fact]
    public async Task TaskTools_AreResolvedOntoTheTask_AndLeaveTheAgentUntouched()
    {
        var (loader, factory, taskRepo, agentRepo, registry) = Build(strictTools: true);
        registry.AddTool("file_read", new MockTool("file_read"));
        registry.AddTool("file_write", new MockTool("file_write"));
        var config = await loader.LoadFromStringAsync(Yaml, TestContext.Current.CancellationToken);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var tasks = new List<Orkeon.Domain.Task.CrewTask>();
        foreach (var id in crew.Tasks)
            tasks.Add((await taskRepo.GetByIdAsync(id, TestContext.Current.CancellationToken))!);
        var report = Assert.Single(tasks, t => t.Description.Value.StartsWith("Write", StringComparison.Ordinal));
        var review = Assert.Single(tasks, t => t.Description.Value.StartsWith("Review", StringComparison.Ordinal));
        Assert.Equal(["file_write"], report.Tools.Select(t => t.Name));
        Assert.Empty(review.Tools);

        var agent = await agentRepo.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.Equal(["file_read"], agent!.Tools.Select(t => t.Name));
    }

    [Fact]
    public async Task StrictMode_UnknownTaskTool_FailsTheLoad_LikeAnAgentTool()
    {
        var (loader, factory, _, _, registry) = Build(strictTools: true);
        registry.AddTool("file_read", new MockTool("file_read"));
        var config = await loader.LoadFromStringAsync(Yaml, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken));

        Assert.Contains("unknown tool(s): file_write", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Available tools: file_read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LenientMode_UnknownTaskTool_IsSkipped()
    {
        var (loader, factory, taskRepo, _, registry) = Build(strictTools: false);
        registry.AddTool("file_read", new MockTool("file_read"));
        var config = await loader.LoadFromStringAsync(Yaml, TestContext.Current.CancellationToken);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        foreach (var id in crew.Tasks)
            Assert.Empty((await taskRepo.GetByIdAsync(id, TestContext.Current.CancellationToken))!.Tools);
    }
}
