using System.Collections.Immutable;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Constants.Protocol;
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

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class SequentialDecisionStreamingTests : IDisposable
{
    private readonly MockAgentExecutionService _executionService = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();
    private readonly MockCrewExecutionHook _hook = new();

    public SequentialDecisionStreamingTests()
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
    [Trait("BR", "R-25")]
    public async System.Threading.Tasks.Task ShouldCarryDecisionAnswers_WhenStreamingEventEndsDecisionTask()
    {
        var decide = DecisionTask();
        var (crew, strategy) = Build([decide]);

        var events = await Stream(strategy, crew);

        var snapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == decide.Id.ToString());
        Assert.NotNull(snapshot.DecisionAnswers);
        var ended = Assert.Single(events, e => e.Kind == RunEventKinds.TaskCompleted && e.TaskId == decide.Id.ToString());
        Assert.True(ended.IsDecision);
        Assert.Equal(snapshot.DecisionAnswers, ended.DecisionAnswers);
    }

    [Fact]
    [Trait("BR", "R-25")]
    [Trait("BR", "R-67")]
    public async System.Threading.Tasks.Task ShouldCarrySkipReason_WhenStreamingEventEndsSkippedTask()
    {
        var decide = DecisionTask();
        var condition = TaskCondition.Create(
            decide.Description.Value, [ConditionClause.OneOf(decide.Decision!, "intent", ["b"])]);
        var branch = new CrewTaskBuilder().Description("Branch").ExpectedOutput("Branch")
            .When(decide.Id, condition).Build();
        var (crew, strategy) = Build([decide, branch]);

        var events = await Stream(strategy, crew);

        var snapshot = Assert.Single(_hook.CompletedTasks, s => s.TaskId == branch.Id.ToString());
        Assert.True(snapshot.Skipped);
        Assert.False(string.IsNullOrEmpty(snapshot.SkipReason));
        var ended = Assert.Single(events, e => e.Kind == RunEventKinds.TaskCompleted && e.TaskId == branch.Id.ToString());
        Assert.True(ended.Skipped);
        Assert.False(ended.IsDecision);
        Assert.Equal(snapshot.SkipReason, ended.SkipReason);
        Assert.StartsWith($"condition of task {branch.Id} did", ended.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-20")]
    public async System.Threading.Tasks.Task ShouldCountDecisionTokens_WhenSequentialDecisionModelReportsUsage()
    {
        _model.RespondAsync = (request, _) => System.Threading.Tasks.Task.FromResult(new DecisionResponse
        {
            Model = "fake",
            Usage = new DecisionUsage { InputTokens = 30, OutputTokens = 12 },
            Answers = request.Questions.Keys.ToImmutableDictionary(
                name => name,
                _ => (DecisionAnswer)new ChoiceAnswer
                {
                    Choice = "a",
                    Confidence = 0.9,
                    Probabilities = ImmutableDictionary<string, double>.Empty.Add("a", 0.9).Add("b", 0.1),
                }),
        });
        var decide = DecisionTask();
        var (crew, strategy) = Build([decide]);
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();

        Orkeon.Domain.Crew.CrewOutput output;
        using (CrewStreamScope.Begin(channel.Writer))
        {
            output = await strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);
        }
        channel.Writer.Complete();
        var events = new List<CrewExecutionEvent>();
        await foreach (var e in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            events.Add(e);

        var ended = Assert.Single(events, e => e.Kind == RunEventKinds.TaskCompleted && e.TaskId == decide.Id.ToString());
        Assert.Equal(42, ended.Tokens);
        Assert.Equal(42, output.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
    }

    private static async System.Threading.Tasks.Task<List<CrewExecutionEvent>> Stream(
        SequentialProcessStrategy strategy, DomainCrew crew)
    {
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();
        using (CrewStreamScope.Begin(channel.Writer))
        {
            await strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);
        }
        channel.Writer.Complete();
        var events = new List<CrewExecutionEvent>();
        await foreach (var e in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            events.Add(e);
        return events;
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
