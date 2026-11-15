using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class AutonomousCapFlagTests : IDisposable
{
    private readonly MockTaskRepository _taskRepo = new();
    private readonly MockAgentRepository _agentRepo = new();
    private readonly MockAgentExecutionService _execService = new();
    private readonly MockManagerAgent _manager = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly MockCrewExecutionHook _hook = new();
    private readonly InMemoryAgentChannel _channel = new(NullLogger<InMemoryAgentChannel>.Instance);

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-39")]
    public async System.Threading.Tasks.Task ShouldFlagTheTask_WhenAnAutonomousRunEndsOnTheIterationCap(bool capped)
    {
        var agent = new AgentBuilder().Role("worker").Goal("Goal").Backstory("Backstory").Build();
        var task = new CrewTaskBuilder().Description("capped work").ExpectedOutput("Complete it").Build();
        var crew = DomainCrew.Create("Autonomous crew", ProcessType.Autonomous);
        _agentRepo.AddAgentToStore(agent);
        crew.AddAgent(agent.Id);
        _taskRepo.AddTaskToStore(task);
        crew.AddTask(task.Id);
        _execService.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { EndedOnIterationCap = capped });
        var strategy = new AutonomousProcessStrategy(
            new CrewStrategyDependencies(_taskRepo, _agentRepo, _execService, _memoryScope),
            _channel,
            _manager,
            TestManagerLlm.Resolver(),
            NullLogger<AutonomousProcessStrategy>.Instance,
            _hook);

        await strategy.ExecuteAutonomousAsync(
            crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == task.Id.Value.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(capped, snapshot.EndedOnIterationCap);
    }

    private (DomainCrew Crew, CrewTask Task) BuildDelegationCrew()
    {
        var delegator = new AgentBuilder().Role("delegator").Goal("Goal").Backstory("Backstory")
            .AllowDelegation(true).Build();
        var helper = new AgentBuilder().Role("helper").Goal("Goal").Backstory("Backstory").Build();
        var task = new CrewTaskBuilder().Description("hard work").ExpectedOutput("Complete it").Build();
        var crew = DomainCrew.Create("Autonomous crew", ProcessType.Autonomous);
        _agentRepo.AddAgentToStore(delegator);
        _agentRepo.AddAgentToStore(helper);
        crew.AddAgent(delegator.Id);
        crew.AddAgent(helper.Id);
        _taskRepo.AddTaskToStore(task);
        crew.AddTask(task.Id);
        _manager.SetAssignResult(new TaskAssignment(task.Id, delegator.Id, "lead", DateTime.UtcNow));
        _execService.SetExecuteFunc((agent, _, _, _) =>
            agent.Id == delegator.Id
                ? new TaskResult(false, "", null, [], TimeSpan.Zero, "needs help")
                : new TaskResult(true, "helper out", null, [], TimeSpan.Zero) { EndedOnIterationCap = true });
        return (crew, task);
    }

    private AutonomousProcessStrategy BuildStrategy(IAgentChannel channel)
        => new(
            new CrewStrategyDependencies(_taskRepo, _agentRepo, _execService, _memoryScope),
            channel,
            _manager,
            TestManagerLlm.Resolver(),
            NullLogger<AutonomousProcessStrategy>.Instance,
            _hook);

    [Fact]
    [Trait("BR", "CH-42")]
    public async System.Threading.Tasks.Task ShouldKeepTheCapFlag_WhenAPeerTakesTheTaskOver()
    {
        var (crew, task) = BuildDelegationCrew();
        var channel = new FakeAgentChannel
        {
            Answer = request => new AgentChannelResponse(
                request.CorrelationId, request.ToAgentId, true, "helper out") { EndedOnIterationCap = true },
        };

        await BuildStrategy(channel).ExecuteAutonomousAsync(
            crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == task.Id.Value.ToString());
        Assert.True(snapshot.Success);
        Assert.True(snapshot.EndedOnIterationCap);
    }

    [Fact]
    [Trait("BR", "CH-42")]
    public async System.Threading.Tasks.Task ShouldSetTheCapFlag_WhenTheLocalChannelAnswersFromACappedTask()
    {
        var (crew, _) = BuildDelegationCrew();
        var channel = new FakeAgentChannel { Inner = _channel };

        await BuildStrategy(channel).ExecuteAutonomousAsync(
            crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        var answer = Assert.Single(channel.Answers);
        Assert.True(answer.Success);
        Assert.True(answer.EndedOnIterationCap);
    }
}
