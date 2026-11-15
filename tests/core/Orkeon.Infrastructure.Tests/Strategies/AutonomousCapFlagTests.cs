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
}
