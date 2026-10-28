using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
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

public sealed class SequentialConditionTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();
    private readonly List<TaskId> _ran = [];
    private readonly Dictionary<TaskId, SimpleExecutionContext> _contexts = [];
    private readonly HashSet<TaskId> _failing = [];

    public SequentialConditionTests()
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
    [Trait("BR", "UC-02")]
    public async System.Threading.Tasks.Task ShouldRunTask_WhenConditionHoldsOnDecisionAnswers()
    {
        var decide = DecisionTask("Decide");
        var branch = Conditioned("Branch", decide, OneOf(decide, "a"));
        var other = Conditioned("Other", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([branch, other, decide], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([branch.Id], _ran);
        Assert.Equal(TaskStatus.Completed, branch.Status);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldSkipTaskWithoutFailure_WhenConditionFalse()
    {
        var decide = DecisionTask("Decide");
        var branch = Conditioned("Branch", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, branch], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal(TaskStatus.Cancelled, branch.Status);
        Assert.Empty(_ran);
        Assert.Null(output.Error);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldLeaveNothingInNextContext_WhenTaskConditionSkipped()
    {
        var decide = DecisionTask("Decide");
        var skipped = Conditioned("Skipped branch", decide, OneOf(decide, "b"));
        var after = new CrewTaskBuilder().Description("After").ExpectedOutput("After")
            .DependsOn(decide).DependsOn(skipped).Build();
        var (crew, strategy) = Build([decide, skipped, after], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([after.Id], _ran);
        var previous = _contexts[after.Id].PreviousOutputs;
        Assert.DoesNotContain(previous, o => o.TaskId == skipped.Id);
        Assert.DoesNotContain(previous, o => o.Content.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldRunJoin_WhenOneBranchRanAndOtherSkipped()
    {
        var decide = DecisionTask("Decide");
        var ran = Conditioned("Ran branch", decide, OneOf(decide, "a"));
        var skipped = Conditioned("Skipped branch", decide, OneOf(decide, "b"));
        var join = new CrewTaskBuilder().Description("Join").ExpectedOutput("Join")
            .DependsOn(ran).DependsOn(skipped).Build();
        var (crew, strategy) = Build([decide, ran, skipped, join], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([ran.Id, join.Id], _ran);
        var previous = _contexts[join.Id].PreviousOutputs;
        Assert.Contains(previous, o => o.TaskId == ran.Id);
        Assert.DoesNotContain(previous, o => o.TaskId == skipped.Id);
        Assert.Equal(TaskStatus.Cancelled, skipped.Status);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldSkipJoin_WhenEveryBranchSkipped()
    {
        var decide = DecisionTask("Decide");
        var first = Conditioned("First branch", decide, OneOf(decide, "b"));
        var second = Conditioned("Second branch", decide, OneOf(decide, "b"));
        var join = new CrewTaskBuilder().Description("Join").ExpectedOutput("Join")
            .DependsOn(first).DependsOn(second).Build();
        var (crew, strategy) = Build([decide, first, second, join], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Empty(_ran);
        Assert.Equal(TaskStatus.Cancelled, join.Status);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public async System.Threading.Tasks.Task ShouldRunOtherwiseTask_WhenNoSiblingConditionHeld()
    {
        var decide = DecisionTask("Decide");
        var sibling = Conditioned("Sibling", decide, OneOf(decide, "b"));
        var otherwise = Conditioned("Otherwise", decide, TaskCondition.Otherwise(decide.Description.Value));
        var (crew, strategy) = Build([decide, sibling, otherwise], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([otherwise.Id], _ran);

        _ran.Clear();
        Answer("b");
        var decide2 = DecisionTask("Decide");
        var sibling2 = Conditioned("Sibling", decide2, OneOf(decide2, "b"));
        var otherwise2 = Conditioned("Otherwise", decide2, TaskCondition.Otherwise(decide2.Description.Value));
        var (crew2, strategy2) = Build([decide2, sibling2, otherwise2], DecisionCircuit.On);

        var output2 = await Run(strategy2, crew2);

        Assert.True(output2.Success, output2.Error);
        Assert.Equal([sibling2.Id], _ran);
        Assert.Equal(TaskStatus.Cancelled, otherwise2.Status);
    }

    [Fact]
    [Trait("BR", "UC-02")]
    public async System.Threading.Tasks.Task ShouldRunSeveralBranches_WhenSeveralConditionsHold()
    {
        var decide = DecisionTask("Decide");
        var first = Conditioned("First", decide, OneOf(decide, "a"));
        var second = Conditioned("Second", decide, OneOf(decide, "a", "b"));
        var third = Conditioned("Third", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, first, second, third], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([first.Id, second.Id], _ran);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public async System.Threading.Tasks.Task ShouldSkipDependants_WhenDecisionTaskFailed()
    {
        _model.Failure = new InvalidOperationException("model down");
        var decide = DecisionTask("Decide");
        var branch = Conditioned("Branch", decide, OneOf(decide, "a"));
        var (crew, strategy) = Build([decide, branch], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.Empty(_ran);
        Assert.Equal(TaskStatus.Failed, decide.Status);
        Assert.Equal(TaskStatus.Cancelled, branch.Status);
        Assert.False(output.Success);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async System.Threading.Tasks.Task ShouldStillBlockDependant_WhenDependencyFailed()
    {
        var first = new CrewTaskBuilder().Description("First").ExpectedOutput("First").Build();
        var second = new CrewTaskBuilder().Description("Second").ExpectedOutput("Second").DependsOn(first).Build();
        _failing.Add(first.Id);
        var (crew, strategy) = Build([first, second], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.Equal([first.Id], _ran);
        Assert.Equal(TaskStatus.Cancelled, second.Status);
        Assert.False(output.Success);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    [Trait("BR", "UC-04")]
    public async System.Threading.Tasks.Task ShouldRunOtherwiseBranchWithoutRequest_WhenCircuitOffAndDefaultFallback()
    {
        var decide = DecisionTask("Decide");
        var sibling = Conditioned("Sibling", decide, OneOf(decide, "a"));
        var otherwise = Conditioned("Otherwise", decide, TaskCondition.Otherwise(decide.Description.Value));
        var (crew, strategy) = Build([decide, sibling, otherwise], DecisionCircuit.Off);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Empty(_model.Requests);
        Assert.Equal([otherwise.Id], _ran);
        Assert.Equal(TaskStatus.Cancelled, sibling.Status);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    [Trait("BR", "UC-04")]
    public async System.Threading.Tasks.Task ShouldRunFallbackOptionBranch_WhenCircuitOffAndExplicitFallback()
    {
        var decide = DecisionTask("Decide", new Dictionary<string, string> { ["intent"] = "b" });
        var branchA = Conditioned("Branch A", decide, OneOf(decide, "a"));
        var branchB = Conditioned("Branch B", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, branchA, branchB], DecisionCircuit.Off);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Empty(_model.Requests);
        Assert.Equal([branchB.Id], _ran);
        Assert.Equal(TaskStatus.Cancelled, branchA.Status);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async System.Threading.Tasks.Task ShouldRunCrewUnchanged_WhenCircuitOffAndNoDecisionTask()
    {
        var first = new CrewTaskBuilder().Description("First").ExpectedOutput("First").Build();
        var second = new CrewTaskBuilder().Description("Second").ExpectedOutput("Second").DependsOn(first).Build();
        var (crew, strategy) = Build([first, second], DecisionCircuit.Off);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([first.Id, second.Id], _ran);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldSkipTask_WhenItsDecisionTaskWasItselfSkipped()
    {
        var root = DecisionTask("Root decide");
        var inner = Conditioned("Inner decide", root, OneOf(root, "b"), withDecision: true);
        var child = Conditioned("Child", inner, OneOf(inner, "a"));
        var other = Conditioned("Other", inner, TaskCondition.Otherwise(inner.Description.Value));
        var (crew, strategy) = Build([root, inner, child, other], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Empty(_ran);
        Assert.Equal(TaskStatus.Cancelled, inner.Status);
        Assert.Equal(TaskStatus.Cancelled, child.Status);
        Assert.Equal(TaskStatus.Cancelled, other.Status);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailRunBeforeFirstTask_WhenConditionedTaskDoesNotDependOnItsDecision()
    {
        var decide = DecisionTask("Decide");
        var loose = new CrewTaskBuilder().Description("Loose").ExpectedOutput("Loose").When(OneOf(decide, "a")).Build();
        var (crew, strategy) = Build([decide, loose], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.False(output.Success);
        Assert.Contains("task 'Loose' is conditioned on decision task 'Decide' but does not depend on it", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailRunBeforeFirstTask_WhenConditionNamesDecisionTaskWithDifferentCase()
    {
        var decide = DecisionTask("Route");
        var condition = TaskCondition.Create("route", [ConditionClause.OneOf(decide.Decision!, "intent", ["a"])]);
        var branch = Conditioned("Branch", decide, condition);
        var (crew, strategy) = Build([decide, branch], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.False(output.Success);
        Assert.Contains("task 'Branch' is conditioned on 'route', which names no decision task of the crew", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailRunBeforeFirstTask_WhenConditionNamesNoDecisionTask()
    {
        var ghost = DecisionTask("Ghost");
        var prep = new CrewTaskBuilder().Description("Prep").ExpectedOutput("Prep").Build();
        var condition = TaskCondition.Create("Ghost", [ConditionClause.OneOf(ghost.Decision!, "intent", ["a"])]);
        var branch = Conditioned("Branch", prep, condition);
        var (crew, strategy) = Build([prep, branch], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.False(output.Success);
        Assert.Contains("task 'Branch' is conditioned on 'Ghost', which names no decision task of the crew", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _executionService.ExecuteTaskCallCount);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldTakeFinalOutputFromLastTaskThatRan_WhenLastDeclaredSkipped()
    {
        var decide = DecisionTask("Decide");
        var a = Conditioned("Branch A", decide, OneOf(decide, "a"));
        var b = Conditioned("Branch B", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, a, b], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Equal([a.Id], _ran);
        Assert.Equal("out-" + a.Id, output.Output);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public async System.Threading.Tasks.Task ShouldReportCrewSuccess_WhenOnlyConditionSkipsOccurred()
    {
        var decide = DecisionTask("Decide");
        var a = Conditioned("Branch A", decide, OneOf(decide, "b"));
        var b = Conditioned("Branch B", decide, OneOf(decide, "b"));
        var (crew, strategy) = Build([decide, a, b], DecisionCircuit.On);

        var output = await Run(strategy, crew);

        Assert.True(output.Success, output.Error);
        Assert.Null(output.Error);
        Assert.Empty(_ran);
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
            NullLogger<SequentialProcessStrategy>.Instance);

        return (builder.Build(), strategy);
    }
}
