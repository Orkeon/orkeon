using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
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

public sealed class SequentialDecisionNameLookupTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();
    private readonly List<TaskId> _ran = [];

    public SequentialDecisionNameLookupTests()
    {
        _model.Respond = request => new DecisionResponse
        {
            Model = "fake",
            Answers = request.Questions.Keys.ToImmutableDictionary(
                name => name,
                _ => (DecisionAnswer)new ChoiceAnswer
                {
                    Choice = "b",
                    Confidence = 0.9,
                    Probabilities = ImmutableDictionary<string, double>.Empty.Add("b", 0.9),
                }),
        };
        _executionService.SetExecuteFunc((_, task, _, _) =>
        {
            _ran.Add(task.TaskId);
            return new TaskResult(true, "out-" + task.TaskId, null, [], TimeSpan.Zero);
        });
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    // BR-07 / UC-02: the gate takes the decision task, not the plain task declared first with the same name.
    [Fact]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldGateOnDecisionTask_WhenPlainTaskSharesItsName()
    {
        var plain = new CrewTaskBuilder().Description("Triage").ExpectedOutput("Plain").Build();
        var decide = DecisionTask("Triage");
        var branch = Conditioned("Branch", decide);
        var (crew, strategy) = Build([plain, decide, branch]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success, output.Error);
        Assert.Equal(TaskStatus.Cancelled, branch.Status);
        Assert.DoesNotContain(branch.Id, _ran);
        Assert.Equal([plain.Id], _ran);
    }

    // BR-07 / UC-02: a task is named by its key when it has one, whatever its description says.
    [Fact]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldGateOnDecisionTask_WhenTasksAreNamedByKeyAndDescriptionsDiffer()
    {
        var decide = new CrewTaskBuilder()
            .Description("Classify the incoming request")
            .ExpectedOutput("An outcome")
            .WithKey("triage")
            .WithDecision(Decision())
            .Build();
        var condition = TaskCondition.Create(
            "triage", [ConditionClause.OneOf(decide.Decision!, "intent", ["a"])]);
        var branch = new CrewTaskBuilder().Description("Branch").ExpectedOutput("Branch")
            .When(decide.Id, condition).Build();
        var (crew, strategy) = Build([decide, branch]);

        var output = await strategy.ExecuteSequentialAsync(
            crew, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(output.Success, output.Error);
        Assert.Equal(TaskStatus.Cancelled, branch.Status);
        Assert.DoesNotContain(branch.Id, _ran);
    }

    private static TaskDecision Decision() =>
        TaskDecision.Create(
            [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
            null, null, false, null);

    private static CrewTask DecisionTask(string name) =>
        new CrewTaskBuilder().Description(name).ExpectedOutput("An outcome").WithDecision(Decision()).Build();

    private static CrewTask Conditioned(string name, CrewTask dependency, CrewTask? spec = null)
    {
        var source = spec ?? dependency;
        var condition = TaskCondition.Create(
            source.Description.Value, [ConditionClause.OneOf(source.Decision!, "intent", ["a"])]);
        return new CrewTaskBuilder().Description(name).ExpectedOutput(name).When(dependency.Id, condition).Build();
    }

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
            NullLogger<SequentialProcessStrategy>.Instance);

        return (builder.Build(), strategy);
    }
}
