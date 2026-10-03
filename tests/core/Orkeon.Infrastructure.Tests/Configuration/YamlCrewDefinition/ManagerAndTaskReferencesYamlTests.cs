using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-33 — what a crew file names is what runs. <c>managerAgent:</c> means something in two modes:
/// Hierarchical (the manager assigns each task and reviews its output) and Consensual (it arbitrates
/// the <c>ManagerDecision</c> fallback). In the four others it was read, then ignored: the agent ran
/// tasks like any other. A <c>managerAgent:</c>, a task's <c>agent:</c> or a dependency that names
/// nothing — a typo — was erased at load: the hierarchical crew was told it "requires a manager
/// agent", the consensual one lost its arbiter, the task ran on another agent or without waiting for
/// what it cited. Each is refused at load now, naming the key as written and what the crew has.
/// </summary>
public sealed class ManagerAndTaskReferencesYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static YamlCrewDefinitionLoader BuildLoader(FakeFileSystemService? fs = null)
        => new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static string Crew(string process, string manager = "chef", string writeAgent = "writer", string dependency = "outline") => $$"""
name: managed
goal: Ship the article
process: {{process}}
managerAgent: {{manager}}
agents:
  chef: { role: Chef, goal: Lead the team }
  writer: { role: Writer, goal: Write the article }
tasks:
  outline:
    description: Outline the article
    expectedOutput: An outline
    agent: writer
  write:
    description: Write the article
    expectedOutput: An article
    agent: {{writeAgent}}
    dependencies: [{{dependency}}]
""";

    private static string Unmanaged(string process, string writeAgent = "writer", string dependency = "outline") =>
        Crew(process, writeAgent: writeAgent, dependency: dependency).Replace("managerAgent: chef\n", "", StringComparison.Ordinal);

    // ── decision 3: a manager agent where the mode uses one ──────────────────────────────

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [InlineData("autonomous")]
    [InlineData("Sequential")]
    public async Task A_manager_agent_in_a_mode_without_one_fails_the_load_naming_the_key_and_the_process(string process)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildLoader().LoadFromStringAsync(Crew(process), Ct));

        Assert.Contains("managerAgent: chef", error.Message, StringComparison.Ordinal);
        Assert.Contains($"process: {process}", error.Message, StringComparison.Ordinal);
        Assert.Contains("process: hierarchical", error.Message, StringComparison.Ordinal);
        Assert.Contains("process: consensual", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_autonomous_crew_is_told_its_manager_is_an_llm()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildLoader().LoadFromStringAsync(Crew("autonomous"), Ct));

        Assert.Contains("default profile", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    public async Task Hierarchical_and_consensual_crews_load_their_manager(string process)
    {
        var config = await BuildLoader().LoadFromStringAsync(Crew(process), Ct);

        Assert.Equal(config.Agents.Single(a => a.Role == "Chef").Id, config.ManagerAgentId);
    }

    [Fact]
    public async Task A_per_entity_crew_with_a_manager_in_a_mode_without_one_is_refused_too()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/c/config.yaml", "name: c\ngoal: G\nprocess: parallel\nmanager_agent: lead");
        fs.AddFile("/crews/c/agents/lead.yaml", "role: Lead\ngoal: G");
        fs.AddFile("/crews/c/tasks/collect.yaml", "description: D\nexpectedOutput: O\nagent: lead");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildLoader(fs).LoadFromDirectoryAsync("/crews/c", Ct));

        Assert.Contains("managerAgent: lead", error.Message, StringComparison.Ordinal);
        Assert.Contains("process: parallel", error.Message, StringComparison.Ordinal);
    }

    // ── decision 4: a manager that names no agent ────────────────────────────────────────

    [Theory]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    public async Task A_manager_agent_naming_no_agent_fails_the_load_listing_the_agents(string process)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(Crew(process, manager: "chief"), Ct));

        Assert.Contains("managerAgent: chief", error.Message, StringComparison.Ordinal);
        Assert.Contains("chef, writer", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("requires a manager agent", error.Message, StringComparison.Ordinal);
    }

    // ── decision 6: a task reference that names nothing ──────────────────────────────────

    [Fact]
    public async Task A_task_whose_agent_names_no_agent_fails_the_load_listing_the_agents()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(Unmanaged("sequential", writeAgent: "writter"), Ct));

        Assert.Contains("'write'", error.Message, StringComparison.Ordinal);
        Assert.Contains("agent: writter", error.Message, StringComparison.Ordinal);
        Assert.Contains("chef, writer", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'outline'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_whose_dependency_names_no_task_fails_the_load_listing_the_tasks()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(Unmanaged("sequential", dependency: "outlines"), Ct));

        Assert.Contains("'write'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'outlines'", error.Message, StringComparison.Ordinal);
        Assert.Contains("outline, write", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_reference_that_names_nothing_is_named_at_once()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(Unmanaged("parallel", writeAgent: "writter", dependency: "outlines"), Ct));

        Assert.Contains("agent: writter", error.Message, StringComparison.Ordinal);
        Assert.Contains("'outlines'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_per_entity_task_file_naming_no_agent_is_refused_by_its_name()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/c/config.yaml", "name: c\ngoal: G\nprocess: sequential");
        fs.AddFile("/crews/c/agents/analyst.yaml", "role: Analyst\ngoal: G");
        fs.AddFile("/crews/c/tasks/collect.yaml", "description: D\nexpectedOutput: O\nagent: analist");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildLoader(fs).LoadFromDirectoryAsync("/crews/c", Ct));

        Assert.Contains("'collect'", error.Message, StringComparison.Ordinal);
        Assert.Contains("agent: analist", error.Message, StringComparison.Ordinal);
        Assert.Contains("analyst", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crew_whose_references_all_resolve_loads_them()
    {
        var config = await BuildLoader().LoadFromStringAsync(Unmanaged("sequential"), Ct);

        var outline = config.Tasks.Single(t => t.Description == "Outline the article");
        var write = config.Tasks.Single(t => t.Description == "Write the article");
        Assert.Equal(config.Agents.Single(a => a.Role == "Writer").Id, write.AssignedAgentId);
        Assert.Equal([outline.Id], write.Dependencies);
    }

    // ── the shared validator, for a configuration built in code ──────────────────────────

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Parallel")]
    [InlineData("Graph")]
    [InlineData("Autonomous")]
    public void The_shared_validator_refuses_a_manager_agent_in_a_mode_without_one_naming_its_role(string process)
    {
        var chef = AgentId.Create();
        var config = new CrewConfiguration
        {
            Name = "managed",
            Goal = "Ship the article",
            Process = ProcessType.From(process),
            ManagerAgentId = chef,
            Agents =
            [
                new AgentConfiguration { Id = chef, Role = "Chef", Goal = "Lead the team", Backstory = "b" },
                new AgentConfiguration { Id = AgentId.Create(), Role = "Writer", Goal = "Write", Backstory = "b" },
            ],
            Tasks = [new TaskConfiguration { Id = TaskId.Create(), Description = "Write the article", ExpectedOutput = "An article" }],
        };

        var result = CrewDefinitionValidator.Validate(config);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'Chef'", error, StringComparison.Ordinal);
        Assert.Contains(process, error, StringComparison.Ordinal);
    }
}
