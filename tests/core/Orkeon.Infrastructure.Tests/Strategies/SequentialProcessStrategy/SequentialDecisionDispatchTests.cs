using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class SequentialDecisionDispatchTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();
    private readonly MockCrewExecutionHook _hook = new();

    public SequentialDecisionDispatchTests()
    {
        _model.Respond = request => new DecisionResponse
        {
            Model = "fake",
            Answers = request.Questions.Keys.ToImmutableDictionary(
                name => name,
                _ => (DecisionAnswer)new ChoiceAnswer
                {
                    Choice = "a",
                    Confidence = 0.9,
                    Probabilities = ImmutableDictionary<string, double>.Empty.Add("a", 0.9).Add("b", 0.1),
                }),
        };
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("BR", "BR-01")]
    public async System.Threading.Tasks.Task ShouldDispatchDecisionTaskWithoutAgent_WhenSequentialRuns()
    {
        var decide = DecisionTask();
        var (crew, strategy) = Build([decide]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success, output.Error);
        Assert.Single(_model.Requests);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Equal(0, _executionService.ExecuteTaskGenericCallCount);
        Assert.Equal(TaskStatus.Completed, decide.Status);
    }

    [Fact]
    [Trait("BR", "BR-01")]
    [Trait("BR", "BR-05")]
    public async System.Threading.Tasks.Task ShouldStoreOutputWithDecisionAndNoAgent_WhenDecisionTaskCompletes()
    {
        var decide = DecisionTask();
        var after = new CrewTaskBuilder().Description("After").ExpectedOutput("After").DependsOn(decide).Build();
        IReadOnlyList<string> seen = [];
        _executionService.SetExecuteFunc((_, _, context, _) =>
        {
            seen = [.. context.PreviousOutputs.Select(o => o.Content)];
            return new TaskResult(true, "done", null, [], TimeSpan.Zero);
        });
        var (crew, strategy) = Build([decide, after]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success, output.Error);
        var stored = output.TaskOutputs[0];
        Assert.Null(stored.AgentId);
        Assert.NotNull(stored.Decision);
        Assert.Equal("a", stored.Decision.Answers["intent"].Chosen);
        Assert.Equal(0.9, stored.Decision.Answers["intent"].Confidence);
        Assert.Equal(TaskStatus.Completed, decide.Status);
        Assert.Equal(1, _executionService.ExecuteTaskCallCount);
        Assert.Equal(stored.Output, Assert.Single(seen));
    }

    [Fact]
    [Trait("BR", "R-02")]
    [Trait("BR", "R-29")]
    public async System.Threading.Tasks.Task ShouldRecordFailedOutput_WhenSequentialDecisionFails()
    {
        _model.Failure = new InvalidOperationException("model down");
        var decide = DecisionTask();
        var (crew, strategy) = Build([decide]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        var stored = Assert.Single(output.TaskOutputs);
        Assert.Equal(decide.Id, stored.TaskId);
        Assert.False(stored.Success);
        Assert.Equal(TaskStatus.Failed, decide.Status);
    }

    [Fact]
    [Trait("BR", "R-26")]
    public async System.Threading.Tasks.Task ShouldMarkSnapshotAsDecision_WhenDecisionFails()
    {
        _model.Failure = new InvalidOperationException("model down");
        var decide = DecisionTask();
        var (crew, strategy) = Build([decide]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == decide.Id.ToString());
        Assert.False(snapshot.Success);
        Assert.True(snapshot.IsDecision);
        var stored = Assert.Single(output.TaskOutputs);
        Assert.Equal("Task failed: model down", stored.RawOutput);
    }

    private static CrewTask DecisionTask() =>
        new CrewTaskBuilder().Description("Decide").ExpectedOutput("An outcome")
            .WithDecision(TaskDecision.Create(
                [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
                null, null, false, null))
            .Build();

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
        var runner = new DecisionTaskRunner(_model, DecisionCircuit.On, NullLogger<DecisionTaskRunner>.Instance);
        var strategy = new SequentialProcessStrategy(
            new CrewStrategyDependencies(taskRepository, agentRepository, _executionService, _memoryScope)
            {
                DecisionRunner = runner,
            },
            delegation,
            NullLogger<SequentialProcessStrategy>.Instance,
            _hook);

        return (builder.Build(), strategy);
    }
}
