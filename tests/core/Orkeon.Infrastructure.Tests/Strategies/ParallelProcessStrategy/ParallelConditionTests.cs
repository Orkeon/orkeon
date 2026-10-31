using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class ParallelConditionTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();
    private readonly List<DomainAgent> _agents = [];
    private readonly Dictionary<TaskId, DomainAgent> _agentOf = [];
    private readonly MockCrewExecutionHook _hook = new();
    private readonly List<TaskId> _ran = [];
    private readonly Dictionary<TaskId, SimpleExecutionContext> _contexts = [];
    private readonly object _gate = new();

    public ParallelConditionTests()
    {
        Answer("a");
        _executionService.SetExecuteFunc((agent, task, context, _) =>
        {
            lock (_gate)
            {
                _ran.Add(task.TaskId);
                _contexts[task.TaskId] = context;
                _agentOf[task.TaskId] = agent;
            }
            return new TaskResult(true, "out-" + task.TaskId, null, [], TimeSpan.Zero);
        });
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    private async System.Threading.Tasks.Task ShapeAsync()
    {
        var decide = DecisionTask("Decide", null);
        var ran = Conditioned("Ran branch", decide, OneOf(decide, "a"));
        var skipped = Conditioned("Skipped branch", decide, OneOf(decide, "b"));
        var join = new CrewTaskBuilder().Description("Join").ExpectedOutput("Join")
            .DependsOn(ran).DependsOn(skipped).Build();
        var (crew, strategy) = Build([decide, ran, skipped, join], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Single(_model.Requests);
        Assert.DoesNotContain(decide.Id, _ran);
        Assert.Equal(TaskStatus.Completed, decide.Status);
        Assert.Equal(TaskStatus.Cancelled, skipped.Status);
        Assert.Equal([ran.Id, join.Id], _ran);
        var previous = _contexts[join.Id].PreviousOutputs;
        Assert.Contains(previous, o => o.TaskId == ran.Id);
        Assert.DoesNotContain(previous, o => o.TaskId == skipped.Id);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldSkipAndJoinByCondition_WhenParallelWavesRun() => await ShapeAsync();

    [Fact]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailRunBeforeFirstTask_WhenParallelCircuitOffAndNoFallback()
    {
        var decide = DecisionTask("Decide", null);
        var (crew, strategy) = Build([decide], DecisionCircuit.Off);

        var output = await RunAsync(strategy, crew);

        Assert.False(output.Success);
        Assert.Contains("Decision task 'Decide' has neither a fallback nor an otherwise task.", output.Error, StringComparison.Ordinal);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Empty(_model.Requests);
        Assert.NotEqual(TaskStatus.Completed, decide.Status);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailRunBeforeFirstTask_WhenParallelConditionedTaskDoesNotDependOnItsDecision()
    {
        var decide = DecisionTask("Decide", null);
        var loose = new CrewTaskBuilder().Description("Loose").ExpectedOutput("Loose").When(OneOf(decide, "a")).Build();
        var (crew, strategy) = Build([decide, loose], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.False(output.Success);
        Assert.Contains("task 'Loose' is conditioned on decision task 'Decide' but does not depend on it", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailRunBeforeFirstTask_WhenParallelConditionNamesNoDecisionTask()
    {
        var ghost = DecisionTask("Ghost", null);
        var prep = new CrewTaskBuilder().Description("Prep").ExpectedOutput("Prep").Build();
        var condition = TaskCondition.Create("Ghost", [ConditionClause.OneOf(ghost.Decision!, "intent", ["a"])]);
        var branch = Conditioned("Branch", prep, condition);
        var (crew, strategy) = Build([prep, branch], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.False(output.Success);
        Assert.Contains("task 'Branch' is conditioned on 'Ghost', which names no decision task of the crew", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldTakeFinalOutputFromLastTaskThatRan_WhenParallelLastDeclaredSkipped()
    {
        var decide = DecisionTask("Decide", null);
        var a = Conditioned("Branch A", decide, OneOf(decide, "a"));
        var b = Conditioned("Branch B", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, a, b], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([a.Id], _ran);
        Assert.Equal("out-" + a.Id, output.Output);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldTakeFinalOutputFromLastDeclaredTask_WhenWavesReorderTasks()
    {
        // Declared A, B (depends on A), C: the waves run [A, C] then [B], so B settles last.
        var a = new CrewTaskBuilder().Description("A").ExpectedOutput("A").Build();
        var b = new CrewTaskBuilder().Description("B").ExpectedOutput("B").DependsOn(a).Build();
        var c = new CrewTaskBuilder().Description("C").ExpectedOutput("C").Build();
        var (crew, strategy) = Build([a, b, c], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal(b.Id, _ran[^1]);
        Assert.Equal("out-" + c.Id, output.Output);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    [Trait("BR", "UC-03")]
    public async System.Threading.Tasks.Task ShouldListSkippedTaskAndDecisionRole_WhenParallelCrewCompletes()
    {
        var decide = DecisionTask("Decide", null);
        var ran = Conditioned("Ran branch", decide, OneOf(decide, "a"));
        var skipped = Conditioned("Skipped branch", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, ran, skipped], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.True(output.Success, output.Error);
        var terminal = Assert.Single(_hook.Completions).Tasks;
        var skippedSnapshot = Assert.Single(terminal, s => s.TaskId == skipped.Id.ToString());
        Assert.True(skippedSnapshot.Skipped);
        Assert.False(skippedSnapshot.Success);
        var decided = Assert.Single(_hook.CompletedTasks, s => s.TaskId == decide.Id.ToString());
        Assert.Equal("decision", decided.AgentRole);
        var seen = Assert.Single(_contexts[ran.Id].PreviousOutputs, o => o.TaskId == decide.Id);
        Assert.Null(seen.AgentId);
    }

    [Fact]
    [Trait("BR", "UC-03")]
    public async System.Threading.Tasks.Task ShouldFillDecisionFlagAndAnswers_WhenDecisionTaskCompleted()
    {
        var decide = DecisionTask("Decide", null);
        var plain = new CrewTaskBuilder().Description("Plain").ExpectedOutput("Plain").Build();
        var (crew, strategy) = Build([decide, plain], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.True(output.Success, output.Error);
        var stored = Assert.Single(output.TaskOutputs, o => o.Decision is not null);
        var decided = Assert.Single(_hook.CompletedTasks, s => s.TaskId == decide.Id.ToString());
        Assert.True(decided.IsDecision);
        Assert.NotNull(decided.DecisionAnswers);
        Assert.Equal(stored.Output, decided.DecisionAnswers);
        Assert.Contains("\"intent\"", decided.DecisionAnswers, StringComparison.Ordinal);
        var plainSnapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == plain.Id.ToString());
        Assert.False(plainSnapshot.IsDecision);
        Assert.Null(plainSnapshot.DecisionAnswers);
    }

    [Fact]
    [Trait("BR", "R-24")]
    public async System.Threading.Tasks.Task ShouldGiveNextAgentToNextRunTask_WhenConditionSkipsTask()
    {
        var decide = DecisionTask("Decide", null);
        var skipped = Conditioned("Skipped branch", decide, OneOf(decide, "b"));
        var run = Conditioned("Run branch", decide, OneOf(decide, "a"));
        var (crew, strategy) = Build([decide, skipped, run], DecisionCircuit.On, agentCount: 2);

        var output = await RunAsync(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([run.Id], _ran);
        Assert.Equal(_agents[0].Id, _agentOf[run.Id].Id);
    }

    [Fact]
    [Trait("BR", "R-27")]
    public async System.Threading.Tasks.Task ShouldRunCrew_WhenEveryTaskIsADecision()
    {
        var decide = DecisionTask("Decide", null);
        var (crew, strategy) = Build([decide], DecisionCircuit.On, agentCount: 0);

        var output = await RunAsync(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Single(_model.Requests);
        Assert.Equal(TaskStatus.Completed, decide.Status);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
    }

    [Fact]
    [Trait("BR", "R-29")]
    public async System.Threading.Tasks.Task ShouldSkipDependants_WhenParallelDecisionFails()
    {
        _model.Failure = new InvalidOperationException("model down");
        var decide = DecisionTask("Decide", null);
        var branch = Conditioned("Branch", decide, OneOf(decide, "a"));
        var (crew, strategy) = Build([decide, branch], DecisionCircuit.On);

        var output = await RunAsync(strategy, crew);

        Assert.Empty(_ran);
        Assert.Equal(TaskStatus.Failed, decide.Status);
        Assert.NotEqual(TaskStatus.Completed, branch.Status);
        Assert.False(output.Success);
    }

    [Fact]
    [Trait("BR", "R-29")]
    public async System.Threading.Tasks.Task ShouldStopWithoutRunningBranches_WhenCancelledDuringDecision()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _model.RespondAsync = async (_, ct) =>
        {
            await cts.CancelAsync();
            await System.Threading.Tasks.Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };
        var decide = DecisionTask("Decide", null);
        var branch = Conditioned("Branch", decide, OneOf(decide, "a"));
        var (crew, strategy) = Build([decide, branch], DecisionCircuit.On);

        var thrown = await Record.ExceptionAsync(() => RunAsync(strategy, crew, cts.Token));

        Assert.True(thrown is null or OperationCanceledException, thrown?.ToString());
        Assert.Empty(_ran);
        Assert.NotEqual(TaskStatus.Completed, branch.Status);
        Assert.NotEqual(TaskStatus.Completed, decide.Status);
    }

    private static System.Threading.Tasks.Task<Orkeon.Domain.Crew.CrewOutput> RunAsync(ParallelProcessStrategy strategy, DomainCrew crew, CancellationToken? token = null) =>
        strategy.ExecuteParallelAsync(crew, cancellationToken: token ?? TestContext.Current.CancellationToken);

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

    private static CrewTask DecisionTask(string name, IReadOnlyDictionary<string, string>? fallback) =>
        new CrewTaskBuilder().Description(name).ExpectedOutput("An outcome")
            .WithDecision(TaskDecision.Create(
                [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
                null, null, false, fallback))
            .Build();

    private static TaskCondition OneOf(CrewTask decide, params string[] options) =>
        TaskCondition.Create(decide.Description.Value, [ConditionClause.OneOf(decide.Decision!, "intent", options)]);

    private static CrewTask Conditioned(string name, CrewTask decide, TaskCondition condition) =>
        new CrewTaskBuilder().Description(name).ExpectedOutput(name).When(decide.Id, condition).Build();

    private (DomainCrew Crew, ParallelProcessStrategy Strategy) Build(IReadOnlyList<CrewTask> tasks, DecisionCircuit circuit, int agentCount = 1)
    {
        _agents.Clear();
        for (var i = 0; i < agentCount; i++)
            _agents.Add(new AgentBuilder().Role("Worker " + i).Goal("Work").Backstory("Works").Build());
        var unitOfWork = new NullUnitOfWork();
        var taskRepository = new InMemoryTaskRepository(unitOfWork);
        var agentRepository = new InMemoryAgentRepository(unitOfWork);
        foreach (var task in tasks)
            taskRepository.AddAsync(task).GetAwaiter().GetResult();
        foreach (var agent in _agents)
            agentRepository.AddAsync(agent).GetAwaiter().GetResult();

        var builder = new CrewBuilder().Goal("Pipeline").Parallel();
        foreach (var agent in _agents)
            builder.WithAgent(agent);
        foreach (var task in tasks)
            builder.WithTask(task);

        var runner = new DecisionTaskRunner(_model, circuit, NullLogger<DecisionTaskRunner>.Instance);
        var dependencies = new CrewStrategyDependencies(taskRepository, agentRepository, _executionService, _memoryScope)
        {
            DecisionRunner = runner,
        };
        var strategy = new ParallelProcessStrategy(dependencies, NullLogger<ParallelProcessStrategy>.Instance, _hook);
        return (builder.Build(), strategy);
    }
}
