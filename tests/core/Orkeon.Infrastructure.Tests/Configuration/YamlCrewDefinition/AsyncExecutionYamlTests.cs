using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-22 — <c>asyncExecution: true</c> means something in two modes only: Sequential runs the task
/// alongside the tasks after it, Parallel already runs every task of a wave at once. The four others
/// order their tasks themselves — the manager, the vote, the graph, the budget —, so a crew that asks
/// it of them is refused at load, naming the task and the two modes that honour it. It used to load
/// and be ignored in every mode. <c>asyncExecution: false</c>, the default, loads everywhere.
/// </summary>
public class AsyncExecutionYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static YamlCrewDefinitionLoader BuildLoader(FakeFileSystemService? fs = null)
        => new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static string Crew(string process, string gatherAsync) => $$"""
name: watch
goal: Watch the market
process: {{process}}
managerAgent: boss
agents:
  boss: { role: Boss, goal: Lead the work }
  analyst: { role: Analyst, goal: Analyze }
tasks:
  gather:
    description: Gather the facts
    expectedOutput: The facts
    agent: analyst
    asyncExecution: {{gatherAsync}}
  write:
    description: Write the report
    expectedOutput: A report
    agent: analyst
    dependencies: [gather]
""";

    [Theory]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    [InlineData("graph")]
    [InlineData("autonomous")]
    [InlineData("Hierarchical")]
    public async Task A_mode_that_orders_its_tasks_itself_refuses_asyncExecution_true_naming_the_task(string process)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(Crew(process, "true"), Ct));

        Assert.Contains("'gather'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("asyncExecution: true", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"process: {process}", ex.Message, StringComparison.Ordinal);
        Assert.Contains("process: sequential", ex.Message, StringComparison.Ordinal);
        Assert.Contains("process: parallel", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'write'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_task_that_asks_for_it_is_named_at_once()
    {
        var yaml = Crew("graph", "true").Replace(
            "    dependencies: [gather]", "    dependencies: [gather]\n    asyncExecution: true", StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(yaml, Ct));

        Assert.Contains("'gather'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'write'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    public async Task Sequential_and_parallel_accept_asyncExecution_true(string process)
    {
        var config = await BuildLoader().LoadFromStringAsync(Crew(process, "true"), Ct);

        Assert.True(config.Tasks.Single(t => t.Description == "Gather the facts").AsyncExecution);
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    [InlineData("graph")]
    [InlineData("autonomous")]
    public async Task AsyncExecution_false_loads_in_every_mode(string process)
    {
        var config = await BuildLoader().LoadFromStringAsync(Crew(process, "false"), Ct);

        Assert.All(config.Tasks, t => Assert.False(t.AsyncExecution));
    }

    [Fact]
    public async Task A_per_entity_task_file_asking_for_it_is_refused_by_its_name()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/c/config.yaml", "name: c\ngoal: G\nprocess: consensual");
        fs.AddFile("/crews/c/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/c/tasks/collect.yaml", "description: D\nexpectedOutput: O\nagent: a\nasync_execution: true");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader(fs).LoadFromDirectoryAsync("/crews/c", Ct));

        Assert.Contains("'collect'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("process: consensual", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuration built in code reaches <c>CrewFactory</c> without the YAML mapper: the shared
    /// validator refuses it there, before anything is built or ingested, naming the task by its
    /// description.
    /// </summary>
    [Theory]
    [InlineData("Hierarchical")]
    [InlineData("Consensual")]
    [InlineData("Graph")]
    [InlineData("Autonomous")]
    public void The_shared_validator_refuses_it_too_in_a_mode_that_orders_its_tasks(string process)
    {
        var result = CrewDefinitionValidator.Validate(Configuration(ProcessType.From(process), asyncExecution: true));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'Gather the facts'", error, StringComparison.Ordinal);
        Assert.Contains("asyncExecution", error, StringComparison.Ordinal);
        Assert.Contains(process, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sequential", true)]
    [InlineData("Parallel", true)]
    [InlineData("Graph", false)]
    public void The_shared_validator_accepts_it_where_it_means_something_and_its_absence_everywhere(string process, bool asyncExecution)
    {
        Assert.True(CrewDefinitionValidator.Validate(Configuration(ProcessType.From(process), asyncExecution)).IsValid);
    }

    private static CrewConfiguration Configuration(ProcessType process, bool asyncExecution)
    {
        var agent = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "watch",
            Goal = "Watch the market",
            Process = process,
            ManagerAgentId = process == ProcessType.Hierarchical ? agent : null,
            Agents = [new AgentConfiguration { Id = agent, Role = "Analyst", Goal = "Analyze", Backstory = "b" }],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "Gather the facts",
                    ExpectedOutput = "The facts",
                    AssignedAgentId = agent,
                    AsyncExecution = asyncExecution,
                },
            ],
        };
    }
}
