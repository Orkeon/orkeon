using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
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

public sealed class SequentialOutcomeTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();
    private readonly List<TaskId> _ran = [];
    private readonly Dictionary<TaskId, SimpleExecutionContext> _contexts = [];
    private readonly HashSet<TaskId> _failing = [];
    private readonly RecordingHook _hook = new();

    public SequentialOutcomeTests()
    {
        Answer("a");
        _executionService.SetExecuteFunc((_, task, context, _) =>
        {
            _ran.Add(task.TaskId);
            _contexts[task.TaskId] = context;
            return _failing.Contains(task.TaskId)
                ? new TaskResult(false, "", null, [], TimeSpan.Zero, "boom")
                : new TaskResult(true, "out-" + task.TaskId, null, [], TimeSpan.Zero);
        });
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    [Trait("BR", "UC-03")]
    public async System.Threading.Tasks.Task ShouldCarrySkipReasonAndCondition_WhenTaskCompletedNotified()
    {
        var decide = DecisionTask("Decide");
        var ran = Conditioned("Ran branch", decide, OneOf(decide, "a"));
        var skipped = Conditioned("Skipped branch", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, ran, skipped], DecisionCircuit.On);

        await Run(strategy, crew);

        var skippedSnapshot = Assert.Single(_hook.Tasks, s => s.TaskId == skipped.Id.ToString());
        Assert.NotNull(skippedSnapshot.SkipReason);
        Assert.Contains("Decide", skippedSnapshot.SkipReason);
        Assert.Contains("intent", skippedSnapshot.SkipReason);
        var ranSnapshot = Assert.Single(_hook.Tasks, s => s.TaskId == ran.Id.ToString());
        Assert.Null(ranSnapshot.SkipReason);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    [Trait("BR", "UC-03")]
    public async System.Threading.Tasks.Task ShouldCarryDefaultedMark_WhenDecisionTaskDefaulted()
    {
        var decideOff = DecisionTask("Decide");
        var branchOff = Conditioned("Branch", decideOff, TaskCondition.Otherwise(decideOff.Description.Value));
        var (crewOff, strategyOff) = Build([decideOff, branchOff], DecisionCircuit.Off);
        await Run(strategyOff, crewOff);
        var defaulted = Assert.Single(_hook.Tasks, s => s.TaskId == decideOff.Id.ToString());
        Assert.True(defaulted.Defaulted);

        _hook.Tasks.Clear();
        var decideOn = DecisionTask("Decide");
        var branchOn = Conditioned("Branch", decideOn, OneOf(decideOn, "a"));
        var (crewOn, strategyOn) = Build([decideOn, branchOn], DecisionCircuit.On);
        await Run(strategyOn, crewOn);
        var answered = Assert.Single(_hook.Tasks, s => s.TaskId == decideOn.Id.ToString());
        Assert.False(answered.Defaulted);
    }

    [Fact]
    [Trait("BR", "UC-03")]
    public async System.Threading.Tasks.Task ShouldFillDecisionFlagAndAnswers_WhenDecisionTaskCompleted()
    {
        var decide = DecisionTask("Decide");
        var plain = new CrewTaskBuilder().Description("Plain").ExpectedOutput("Plain").Build();
        var (crew, strategy) = Build([decide, plain], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        var stored = Assert.Single(output.TaskOutputs, o => o.Decision is not null);
        var decided = Assert.Single(_hook.Tasks, s => s.TaskId == decide.Id.ToString());
        Assert.True(decided.IsDecision);
        Assert.Equal("decision", decided.AgentRole);
        Assert.NotNull(decided.DecisionAnswers);
        Assert.Equal(stored.Output, decided.DecisionAnswers);
        Assert.Contains("\"intent\"", decided.DecisionAnswers, StringComparison.Ordinal);
        var seen = Assert.Single(_contexts[plain.Id].PreviousOutputs, o => o.TaskId == decide.Id);
        Assert.Null(seen.AgentId);
        var plainSnapshot = Assert.Single(_hook.Tasks, s => s.TaskId == plain.Id.ToString());
        Assert.False(plainSnapshot.IsDecision);
        Assert.Null(plainSnapshot.DecisionAnswers);
    }

    private static System.Threading.Tasks.Task<Orkeon.Domain.Crew.CrewOutput> Run(SequentialProcessStrategy strategy, DomainCrew crew) =>
        strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

    private void Answer(string choice) =>
        _model.Respond = request => new DecisionResponse
        {
            Model = "fake",
            Answers = request.Questions.Keys.ToImmutableDictionary(
                name => name,
                _ => (DecisionAnswer)new ChoiceAnswer
                {
                    Choice = choice,
                    Confidence = 0.9,
                    Probabilities = ImmutableDictionary<string, double>.Empty.Add(choice, 0.9),
                }),
        };

    private static TaskDecision Decision(IReadOnlyDictionary<string, string>? fallback) =>
        TaskDecision.Create(
            [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
            null, null, false, fallback);

    private static CrewTask DecisionTask(string name, IReadOnlyDictionary<string, string>? fallback = null) =>
        new CrewTaskBuilder().Description(name).ExpectedOutput("An outcome").WithDecision(Decision(fallback)).Build();

    private static TaskCondition OneOf(CrewTask decide, params string[] options) =>
        TaskCondition.Create(decide.Description.Value, [ConditionClause.OneOf(decide.Decision!, "intent", options)]);

    private static CrewTask Conditioned(string name, CrewTask decide, TaskCondition condition, bool withDecision = false)
    {
        var builder = new CrewTaskBuilder().Description(name).ExpectedOutput(name).When(decide.Id, condition);
        if (withDecision)
            builder.WithDecision(Decision(null));
        return builder.Build();
    }

    private (DomainCrew Crew, SequentialProcessStrategy Strategy) Build(IReadOnlyList<CrewTask> tasks, DecisionCircuit circuit)
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
        var runner = new DecisionTaskRunner(_model, circuit, NullLogger<DecisionTaskRunner>.Instance);
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

    private sealed class RecordingHook : ICrewExecutionHook
    {
        public List<TaskExecutionSnapshot> Tasks { get; } = [];

        public System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
        {
            Tasks.Add(snapshot);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task OnCrewFailedAsync(CrewExecutionSnapshot snapshot, Exception? ex, CancellationToken ct) =>
            System.Threading.Tasks.Task.CompletedTask;
    }
}
