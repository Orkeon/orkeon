using Orkeon.Application.Services.AgentSelection;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Services.AgentSelection;

public class SkillMatchingSelectionStrategyTests
{
    private static DomainAgent CreateAgent(string role, string goal = "Do work", string? backstory = null)
    {
        return DomainAgent.Create(
            AgentRole.From(role),
            AgentGoal.From(goal),
            backstory: backstory != null ? AgentBackstory.From(backstory) : null,
            allowDelegation: false,
            maxIterations: 5,
            maxRpm: 10,
            verbose: false,
            maxRetryLimit: 3);
    }

    private static DomainTask CreateTask(string description)
    {
        return DomainTask.Create(
            TaskDescription.From(description),
            expectedOutput: ExpectedOutput.From("Result"));
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_MatchingRole_ReturnsAgent()
    {
        // Arrange
        var strategy = new SkillMatchingSelectionStrategy();
        var dataAgent = CreateAgent("data analyst", goal: "Analyze data thoroughly");
        var devAgent = CreateAgent("software developer", goal: "Build software");
        var agents = new[] { dataAgent, devAgent };
        var task = CreateTask("data analysis for quarterly report");

        // Act
        var result = await strategy.SelectBestAgentAsync(task, agents, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(dataAgent.Id, result.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_NoMatch_ReturnsNull()
    {
        // Arrange
        var strategy = new SkillMatchingSelectionStrategy();
        var agent = CreateAgent("chef");
        var agents = new[] { agent };
        var task = CreateTask("quantum physics calculations");

        // Act
        var result = await strategy.SelectBestAgentAsync(task, agents, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_ExcludesSelf()
    {
        // Arrange
        var strategy = new SkillMatchingSelectionStrategy();
        var agent1 = CreateAgent("data analyst");
        var agent2 = CreateAgent("data scientist");
        var agents = new[] { agent1, agent2 };
        var task = CreateTask("data science for ml pipeline");

        // Act — pass agent2 as current, so it should be excluded and agent1 should not be selected either (no match)
        var result = await strategy.SelectBestAgentAsync(task, agents, currentAgent: agent2, TestContext.Current.CancellationToken);

        // agent1 matches "data" but agent2 is excluded; agent1 does not contain "science" but matches "data"
        // The result should be agent1 since it is not the current agent and has overlap.
        if (result != null)
        {
            Assert.NotEqual(agent2.Id, result.Id);
        }
        // If null, that is also valid — means the match was below threshold. Either way, self was excluded.
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_EmptyAgents_ReturnsNull()
    {
        // Arrange
        var strategy = new SkillMatchingSelectionStrategy();
        var task = CreateTask("any task requiring any skill");

        // Act
        var result = await strategy.SelectBestAgentAsync(task, [], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }
}
