using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Parsing;

namespace Orkeon.Infrastructure.Tests.Parsing;

/// <summary>
/// SONAR-14: pins the hybrid execution-plan parser — the JSON happy path with
/// dependencies, parallel groups and agent assignment; the skip rules for malformed
/// or unknown entries; and the "KEY: value" text fallback with its own skip rules.
/// </summary>
public class ExecutionPlanParserTests
{
    private readonly ExecutionPlanParser _parser = new(NullLogger<ExecutionPlanParser>.Instance);

    private static (List<TaskId> tasks, List<AgentId> agents) BuildIds(int taskCount = 2, int agentCount = 1)
    {
        var tasks = Enumerable.Range(0, taskCount).Select(_ => TaskId.Create()).ToList();
        var agents = Enumerable.Range(0, agentCount).Select(_ => AgentId.Create()).ToList();
        return (tasks, agents);
    }

    // ── JSON path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ParsesAFullJsonPlan_WithDependenciesGroupAndAgent()
    {
        var (tasks, agents) = BuildIds();
        var json = $$"""
        {
          "tasks": [
            { "task": "{{tasks[0]}}", "order": 1, "instructions": "start here", "agent": "{{agents[0]}}" },
            { "task": "{{tasks[1]}}", "order": 2, "parallel_group": 7, "dependencies": ["{{tasks[0]}}"] }
          ]
        }
        """;

        var plan = await _parser.ParseAsync(json, tasks, agents, TestContext.Current.CancellationToken);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Tasks.Count);

        var first = plan.Tasks.Single(t => t.TaskId == tasks[0]);
        Assert.Equal(1, first.ExecutionOrder);
        Assert.Equal("start here", first.Instructions);
        Assert.Null(first.ParallelGroup);
        Assert.Equal(agents[0], plan.GetAssignedAgent(tasks[0]));

        var second = plan.Tasks.Single(t => t.TaskId == tasks[1]);
        Assert.Equal(7, second.ParallelGroup);
        Assert.Equal(tasks[0], Assert.Single(second.Dependencies));
        Assert.Null(plan.GetAssignedAgent(tasks[1]));
    }

    [Fact]
    public async Task SkipsJsonEntries_WithUnknownTask_MissingOrder_OrUnknownReferences()
    {
        var (tasks, agents) = BuildIds();
        var unknown = TaskId.Create();
        var json = $$"""
        {
          "tasks": [
            { "task": "{{unknown}}", "order": 1 },
            { "task": "{{tasks[0]}}" },
            { "task": "{{tasks[1]}}", "order": 3,
              "dependencies": ["{{unknown}}", 42],
              "agent": "{{AgentId.Create()}}" }
          ]
        }
        """;

        var plan = await _parser.ParseAsync(json, tasks, agents, TestContext.Current.CancellationToken);

        // Only the third entry survives; its unknown dependency and agent are dropped.
        var planned = Assert.Single(plan!.Tasks);
        Assert.Equal(tasks[1], planned.TaskId);
        Assert.Empty(planned.Dependencies);
        Assert.Null(plan.GetAssignedAgent(tasks[1]));
    }

    [Theory]
    [InlineData("""{ "plan": [] }""")]           // no "tasks" property
    [InlineData("""{ "tasks": "not an array" }""")]
    [InlineData("""{ "tasks": [] }""")]           // empty → JSON path yields no plan
    public async Task FallsBackToTextParsing_WhenTheJsonShapeIsUnusable(string json)
    {
        var (tasks, agents) = BuildIds();

        var plan = await _parser.ParseAsync(json, tasks, agents, TestContext.Current.CancellationToken);

        // The text fallback finds no "TASK:/ORDER:" blocks either — empty plan.
        Assert.NotNull(plan);
        Assert.Empty(plan!.Tasks);
    }

    [Fact]
    public async Task ToleratesNonStringJsonValues_WhereStringsAreExpected()
    {
        // LLM output is untrusted: numbers in string positions must degrade
        // gracefully (skip / drop), never throw out of GetString().
        var (tasks, agents) = BuildIds();
        var json = $$"""
        {
          "tasks": [
            { "task": 42, "order": 1 },
            { "task": "{{tasks[0]}}", "order": 1, "instructions": 42, "agent": 42,
              "dependencies": [42, null, "{{tasks[1]}}"] }
          ]
        }
        """;

        var plan = await _parser.ParseAsync(json, tasks, agents, TestContext.Current.CancellationToken);

        var planned = Assert.Single(plan!.Tasks);
        Assert.Equal(tasks[0], planned.TaskId);
        Assert.Null(planned.Instructions);
        Assert.Equal(tasks[1], Assert.Single(planned.Dependencies));
        Assert.Null(plan.GetAssignedAgent(tasks[0]));
    }

    // ── Text fallback path ────────────────────────────────────────────────

    [Fact]
    public async Task ParsesTextBlocks_WithAllKeys()
    {
        var (tasks, agents) = BuildIds();
        var text = $"""
        TASK: {tasks[0]}
        ORDER: 1
        INSTRUCTIONS: gather sources
        AGENT: {agents[0]}
        ---
        TASK: {tasks[1]}
        ORDER: 2
        PARALLEL_GROUP: 3
        DEPENDENCIES: {tasks[0]}, {TaskId.Create()}
        """;

        var plan = await _parser.ParseAsync(text, tasks, agents, TestContext.Current.CancellationToken);

        Assert.Equal(2, plan!.Tasks.Count);
        var first = plan.Tasks.Single(t => t.TaskId == tasks[0]);
        Assert.Equal("gather sources", first.Instructions);
        Assert.Equal(agents[0], plan.GetAssignedAgent(tasks[0]));

        var second = plan.Tasks.Single(t => t.TaskId == tasks[1]);
        Assert.Equal(3, second.ParallelGroup);
        // The unknown dependency is dropped, the known one kept.
        Assert.Equal(tasks[0], Assert.Single(second.Dependencies));
    }

    [Fact]
    public async Task SkipsTextBlocks_WithMissingOrUnparsableFields()
    {
        var (tasks, agents) = BuildIds();
        var text = $"""
        TASK: {tasks[0]}
        ---
        TASK: {tasks[0]}
        ORDER: not-a-number
        ---
        TASK: {TaskId.Create()}
        ORDER: 1
        ---
        TASK: {tasks[1]}
        ORDER: 2
        PARALLEL_GROUP: nope
        DEPENDENCIES:
        AGENT: {AgentId.Create()}
        """;

        var plan = await _parser.ParseAsync(text, tasks, agents, TestContext.Current.CancellationToken);

        // Only the last block survives: group unparsable → null, deps blank → empty,
        // agent unknown → unassigned.
        var planned = Assert.Single(plan!.Tasks);
        Assert.Equal(tasks[1], planned.TaskId);
        Assert.Null(planned.ParallelGroup);
        Assert.Empty(planned.Dependencies);
        Assert.Null(plan.GetAssignedAgent(tasks[1]));
    }

    [Fact]
    public async Task FreeProse_YieldsAnEmptyPlan()
    {
        var (tasks, agents) = BuildIds();

        var plan = await _parser.ParseAsync(
            "I think the crew should start with research.", tasks, agents,
            TestContext.Current.CancellationToken);

        Assert.NotNull(plan);
        Assert.Empty(plan!.Tasks);
    }

    // ── Guards ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GuardsItsArguments_AndHonoursCancellation()
    {
        var (tasks, agents) = BuildIds();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _parser.ParseAsync(null!, tasks, agents, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _parser.ParseAsync("{}", null!, agents, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _parser.ParseAsync("{}", tasks, null!, TestContext.Current.CancellationToken));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _parser.ParseAsync("{}", tasks, agents, cts.Token));
    }

    [Fact]
    public void ExposesItsParserName()
    {
        Assert.Equal("HybridExecutionPlanParser", _parser.ParserName);
        Assert.Equal("HybridExecutionPlanParser", new ExecutionPlanParser().ParserName);
    }
}
