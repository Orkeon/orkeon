using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.AgentSelection;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Tests.Services.AgentSelection;

/// <summary>
/// SONAR-14: pins the strategy-composing selection service — argument guards, the
/// empty-pool and below-threshold failures, the successful delegation to the strategy,
/// and the capability/requirement projections.
/// </summary>
public class StrategyAgentSelectionServiceTests
{
    /// <summary>Scripted strategy recording what the service hands it.</summary>
    private sealed class ScriptedSelectionStrategy : IAgentSelectionStrategy
    {
        public DomainAgent? Result { get; set; }
        public ICrewTask? ReceivedTask { get; private set; }
        public List<DomainAgent> ReceivedAgents { get; } = [];
        public DomainAgent? ReceivedCurrentAgent { get; private set; }

        public System.Threading.Tasks.Task<DomainAgent?> SelectBestAgentAsync(
            ICrewTask task,
            IEnumerable<DomainAgent> availableAgents,
            DomainAgent? currentAgent = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedTask = task;
            ReceivedAgents.AddRange(availableAgents);
            ReceivedCurrentAgent = currentAgent;
            return System.Threading.Tasks.Task.FromResult(Result);
        }
    }

    private static DomainAgent BuildAgent(string role = "Analyst", string goal = "Analyze data") =>
        new AgentBuilder().Role(role).Goal(goal).Build();

    private static CrewTask BuildTask() =>
        CrewTask.Create(TaskDescription.From("Find the answer"), ExpectedOutput.From("A report"));

    [Fact]
    public void TheConstructor_RejectsANullStrategy()
    {
        Assert.Throws<ArgumentNullException>(() => new StrategyAgentSelectionService(null!));
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_GuardsItsArguments()
    {
        var service = new StrategyAgentSelectionService(new ScriptedSelectionStrategy());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SelectBestAgentAsync(null!, BuildTask(), embedder: null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SelectBestAgentAsync([BuildAgent()], null!, embedder: null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_Fails_WhenThePoolIsEmpty()
    {
        var strategy = new ScriptedSelectionStrategy();
        var service = new StrategyAgentSelectionService(strategy);

        var result = await service.SelectBestAgentAsync(
            [], BuildTask(), embedder: null!, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("No agents available", result.Reason);
        Assert.Null(strategy.ReceivedTask); // the strategy is never consulted
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_Fails_WhenTheStrategyFindsNoMatch()
    {
        var strategy = new ScriptedSelectionStrategy { Result = null };
        var service = new StrategyAgentSelectionService(strategy);

        var result = await service.SelectBestAgentAsync(
            [BuildAgent()], BuildTask(), embedder: null!, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("threshold", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectBestAgent_ReturnsTheStrategyWinner()
    {
        var winner = BuildAgent("Researcher", "Find data");
        var other = BuildAgent();
        var strategy = new ScriptedSelectionStrategy { Result = winner };
        var service = new StrategyAgentSelectionService(strategy);
        var task = BuildTask();

        var result = await service.SelectBestAgentAsync(
            [other, winner], task, embedder: null!, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(winner.Id, result.SelectedAgentId);
        Assert.Same(task, strategy.ReceivedTask);
        Assert.Equal(2, strategy.ReceivedAgents.Count);
        Assert.Null(strategy.ReceivedCurrentAgent);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetAgentCapability_ProjectsRoleAndGoal()
    {
        var service = new StrategyAgentSelectionService(new ScriptedSelectionStrategy());
        var agent = BuildAgent("Writer", "Write prose");

        var capability = await service.GetAgentCapabilityAsync(
            agent, embedder: null!, TestContext.Current.CancellationToken);

        Assert.Equal("Writer", capability.Name);
        Assert.Equal("Write prose", capability.Description);
        Assert.Equal(1.0, capability.ConfidenceLevel);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GetAgentCapabilityAsync(null!, embedder: null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task GetTaskRequirement_ProjectsDescriptionAndExpectedOutput()
    {
        var service = new StrategyAgentSelectionService(new ScriptedSelectionStrategy());
        var task = BuildTask();

        var requirement = await service.GetTaskRequirementAsync(
            task, embedder: null!, TestContext.Current.CancellationToken);

        Assert.Equal("Find the answer", requirement.Name);
        Assert.Equal("A report", requirement.Description);
        Assert.Equal(Orkeon.Domain.Task.ValueObjects.RequirementType.Capability, requirement.Type);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GetTaskRequirementAsync(null!, embedder: null!, TestContext.Current.CancellationToken));
    }
}
