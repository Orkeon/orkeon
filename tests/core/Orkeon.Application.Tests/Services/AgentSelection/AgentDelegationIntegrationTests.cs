using Orkeon.Application.Services.AgentSelection;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Services.AgentSelection;

public class AgentDelegationIntegrationTests
{
    private static DomainAgent CreateDelegatingAgent(string role, string goal = "Delegate work")
    {
        return DomainAgent.Create(
            AgentRole.From(role),
            AgentGoal.From(goal),
            allowDelegation: true,
            maxIterations: 5,
            maxRpm: 10,
            verbose: false,
            maxRetryLimit: 3);
    }

    private static DomainAgent CreateWorkerAgent(string role, string goal = "Execute tasks")
    {
        return DomainAgent.Create(
            AgentRole.From(role),
            AgentGoal.From(goal),
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
    public async System.Threading.Tasks.Task Agent_WithStrategy_DelegatesToBestAgent()
    {
        // Arrange
        var manager = CreateDelegatingAgent("manager");
        var dataAgent = CreateWorkerAgent("data scientist");
        var agents = new List<DomainAgent> { manager, dataAgent };
        var agentIds = agents.Select(a => a.Id).ToList();

        var strategy = new SkillMatchingSelectionStrategy();

        // Wire up the delegate: inject strategy into manager
        // The strategy still works with full agents, but the domain method receives AgentIds.
        // We build a lookup so the strategy can resolve agents from IDs.
        manager.SetAgentSelectionStrategy(async (task, availableIds, ct) =>
        {
            var result = await strategy.SelectBestAgentAsync(task, agents, manager, ct);
            return result?.Id;
        });

        var task = CreateTask("data science machine learning work");

        // Act
        var decision = await manager.ShouldDelegateAsync(task, agentIds, TestContext.Current.CancellationToken);

        // Assert: manager found a better agent (dataAgent matches "data scientist" against task)
        Assert.True(decision.ShouldDelegate);
        Assert.NotNull(decision.DelegateToAgentId);
        Assert.Equal(dataAgent.Id, decision.DelegateToAgentId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Agent_WithoutStrategy_UsesFallback()
    {
        // Arrange: no SetAgentSelectionStrategy called — uses built-in fallback
        var manager = CreateDelegatingAgent("manager");
        // Worker whose role is contained in the task description (triggers fallback matching)
        var analyst = CreateWorkerAgent("analyst");
        var agentIds = new List<AgentId> { manager.Id, analyst.Id };

        var task = CreateTask("analyst work on the dataset");

        // Act
        var decision = await manager.ShouldDelegateAsync(task, agentIds, TestContext.Current.CancellationToken);

        // Assert: fallback should find an agent ID that is not the manager
        Assert.True(decision.ShouldDelegate);
        Assert.NotNull(decision.DelegateToAgentId);
        Assert.Equal(analyst.Id, decision.DelegateToAgentId);
    }
}
