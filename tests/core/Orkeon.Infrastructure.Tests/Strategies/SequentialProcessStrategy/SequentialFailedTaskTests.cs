using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// STUDIO-12 C5a — a sequential crew with a failed task is a failed crew. It used to report
/// success whatever its tasks did, so an agent that answered with nothing went green all the
/// way to the runner's exit code and to an AUTO_SUMMARY.md saying "Completed" over an empty
/// output mount.
/// </summary>
public sealed class SequentialFailedTaskTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly RecordingHook _hook = new();

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_task_that_failed_fails_the_crew_and_names_the_reason()
    {
        var first = NewTask("extract");
        var second = NewTask("consolidate");
        _executionService.SetExecuteFunc((_, task, _, _) => task.Description.Value == "consolidate"
            ? new TaskResult(false, "", null, [], TimeSpan.Zero, Error: "no final answer")
            {
                ExitReason = AgentExitReason.EmptyFinalAnswer,
                LastError = "no final answer",
            }
            : new TaskResult(true, "extracted", null, [], TimeSpan.Zero));
        var (crew, strategy) = Build([first, second]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.NotNull(output.Error);
        Assert.Contains(second.Id.ToString(), output.Error, StringComparison.Ordinal);
        Assert.Contains("no final answer", output.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Id.ToString(), output.Error, StringComparison.Ordinal);

        // Every task still ran and is reported; the failed one is the last output.
        Assert.Equal(2, output.TaskOutputs.Count);
        Assert.True(output.TaskOutputs[0].Success);
        Assert.False(output.TaskOutputs[1].Success);

        // The hook heard the failure with the reason — AUTO_SUMMARY.md and the error run
        // event read exactly this — and never a completion.
        Assert.Equal(0, _hook.Completions);
        var failure = Assert.Single(_hook.Failures);
        Assert.Equal(CrewHookStatus.Failed, failure.Status);
        Assert.Equal(output.Error, failure.FailureReason);
        Assert.Equal(2, failure.Tasks.Count);
    }

    [Fact]
    public async Task A_crew_whose_tasks_all_succeeded_still_completes()
    {
        _executionService.SetExecuteSuccess("fine");
        var (crew, strategy) = Build([NewTask("a"), NewTask("b")]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success);
        Assert.Null(output.Error);
        Assert.Equal(1, _hook.Completions);
        Assert.Empty(_hook.Failures);
    }

    private static CrewTask NewTask(string name) =>
        new CrewTaskBuilder().Description(name).ExpectedOutput(name).Build();

    private (DomainCrew Crew, SequentialProcessStrategy Strategy) Build(IReadOnlyList<CrewTask> tasks)
    {
        var agent = new AgentBuilder().Role("Worker").Goal("Work").Backstory("Works").Build();
        var unitOfWork = new NullUnitOfWork();
        var taskRepository = new InMemoryTaskRepository(unitOfWork);
        var agentRepository = new InMemoryAgentRepository(unitOfWork);
        foreach (var task in tasks)
            taskRepository.AddAsync(task).GetAwaiter().GetResult();
        agentRepository.AddAsync(agent).GetAwaiter().GetResult();

        var builder = new CrewBuilder().Goal("Pipeline").Sequential().WithAgent(agent);
        foreach (var task in tasks)
            builder.WithTask(task);

        var delegation = new AgentDelegationToolsProvider(
            new MockAgentCommunicationService(), _executionService, NullLogger<AgentDelegationToolsProvider>.Instance);
        var strategy = new SequentialProcessStrategy(
            new CrewStrategyDependencies(taskRepository, agentRepository, _executionService, _memoryScope),
            delegation,
            NullLogger<SequentialProcessStrategy>.Instance,
            _hook);

        return (builder.Build(), strategy);
    }

    private sealed class RecordingHook : ICrewExecutionHook
    {
        public int Completions { get; private set; }
        public List<CrewExecutionSnapshot> Failures { get; } = [];

        public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

        public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
        {
            Completions++;
            return Task.CompletedTask;
        }

        public Task OnCrewFailedAsync(CrewExecutionSnapshot snapshot, Exception? ex, CancellationToken ct)
        {
            Failures.Add(snapshot);
            return Task.CompletedTask;
        }
    }
}
