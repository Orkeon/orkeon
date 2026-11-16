using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class ParallelCapFlagTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly MockCrewExecutionHook _hook = new();

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-25")]
    public async System.Threading.Tasks.Task ShouldCarryTheCapFlagIntoTheSnapshot(bool capped)
    {
        _executionService.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { EndedOnIterationCap = capped });
        var agent = new AgentBuilder().Role("Worker").Goal("Work").Backstory("Works").Build();
        var task = new CrewTaskBuilder().Description("Only").ExpectedOutput("Only").Build();
        var unitOfWork = new NullUnitOfWork();
        var taskRepository = new InMemoryTaskRepository(unitOfWork);
        var agentRepository = new InMemoryAgentRepository(unitOfWork);
        await taskRepository.AddAsync(task, TestContext.Current.CancellationToken);
        await agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);
        var crew = new CrewBuilder().Goal("Pipeline").Parallel().WithAgent(agent).WithTask(task).Build();
        var strategy = new ParallelProcessStrategy(
            new CrewStrategyDependencies(taskRepository, agentRepository, _executionService, _memoryScope),
            NullLogger<ParallelProcessStrategy>.Instance,
            _hook);

        await strategy.ExecuteParallelAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == task.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(capped, snapshot.EndedOnIterationCap);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldCarryTheMissingDeliverableIntoTheSnapshot(bool missing)
    {
        _executionService.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { DeliverableMissing = missing });
        var agent = new AgentBuilder().Role("Worker").Goal("Work").Backstory("Works").Build();
        var task = new CrewTaskBuilder().Description("Only").ExpectedOutput("Only").Build();
        var unitOfWork = new NullUnitOfWork();
        var taskRepository = new InMemoryTaskRepository(unitOfWork);
        var agentRepository = new InMemoryAgentRepository(unitOfWork);
        await taskRepository.AddAsync(task, TestContext.Current.CancellationToken);
        await agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);
        var crew = new CrewBuilder().Goal("Pipeline").Parallel().WithAgent(agent).WithTask(task).Build();
        var strategy = new ParallelProcessStrategy(
            new CrewStrategyDependencies(taskRepository, agentRepository, _executionService, _memoryScope),
            NullLogger<ParallelProcessStrategy>.Instance,
            _hook);

        await strategy.ExecuteParallelAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == task.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(missing, snapshot.DeliverableMissing);
    }
}
