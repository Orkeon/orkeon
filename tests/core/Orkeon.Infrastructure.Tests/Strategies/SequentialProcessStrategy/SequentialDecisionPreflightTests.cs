using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
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

public sealed class SequentialDecisionPreflightTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "UC-04")]
    public async System.Threading.Tasks.Task ShouldFailRunBeforeFirstTask_WhenCircuitOffAndDecisionHasNoFallbackNorOtherwise()
    {
        var decide = DecisionTask(null);
        var (crew, strategy) = Build([decide]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.Contains("Decision task 'Decide' has neither a fallback nor an otherwise task.", output.Error, StringComparison.Ordinal);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Empty(_model.Requests);
        Assert.NotEqual(TaskStatus.Completed, decide.Status);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    [Trait("BR", "UC-04")]
    public async System.Threading.Tasks.Task ShouldStartRun_WhenCircuitOffAndEveryDecisionHasFallbackOrOtherwise()
    {
        var decide = DecisionTask(new Dictionary<string, string> { ["intent"] = "a" });
        var (crew, strategy) = Build([decide]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success, output.Error);
        Assert.Empty(_model.Requests);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Equal(TaskStatus.Completed, decide.Status);
        Assert.True(Assert.Single(output.TaskOutputs).Decision!.IsDefaulted);
    }

    private static CrewTask DecisionTask(IReadOnlyDictionary<string, string>? fallback) =>
        new CrewTaskBuilder().Description("Decide").ExpectedOutput("An outcome")
            .WithDecision(TaskDecision.Create(
                [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
                null, null, false, fallback))
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
        var runner = new DecisionTaskRunner(_model, DecisionCircuit.Off, NullLogger<DecisionTaskRunner>.Instance);
        var strategy = new SequentialProcessStrategy(
            new CrewStrategyDependencies(taskRepository, agentRepository, _executionService, _memoryScope)
            {
                DecisionRunner = runner,
            },
            delegation,
            NullLogger<SequentialProcessStrategy>.Instance);

        return (builder.Build(), strategy);
    }
}
