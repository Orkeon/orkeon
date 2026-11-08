using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// FWK-28 (ADR-018, point 6): the edges a crew's <c>graph:</c> block describes run on the Domain engine.
/// A conditional edge follows the answer of the decision task it leaves, the edge without a <c>when</c>
/// is the "otherwise", a loop is a node entered again, and a failed node ends the run.
/// </summary>
public sealed class GraphDecisionEdgeTests : IDisposable
{
    private readonly MockAgentExecutionService _execution = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly FakeDecisionModel _model = new();
    private readonly MockGraphNodeObserver _observer = new();
    private readonly List<string> _ran = [];
    private readonly HashSet<string> _failing = [];

    public GraphDecisionEdgeTests()
    {
        _execution.SetExecuteFunc((_, task, _, _) =>
        {
            var name = task.Description.Value;
            lock (_ran)
                _ran.Add(name);
            return _failing.Contains(name)
                ? new TaskResult(false, "", null, [], TimeSpan.Zero, Error: name + " failed")
                : new TaskResult(true, "out-" + name, null, [], TimeSpan.Zero);
        });
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CrewTask Keyed(string key) =>
        new CrewTaskBuilder().Description(key).ExpectedOutput(key).WithKey(key).Build();

    private static CrewTask Decision(string key) =>
        new CrewTaskBuilder().Description(key).ExpectedOutput("An outcome").WithKey(key)
            .WithDecision(TaskDecision.Create(
                [ChoiceQuestionSpec.Create("verdict", "Pick", new Dictionary<string, string> { ["approve"] = "ship it", ["revise"] = "again" })],
                null, null, false, null))
            .Build();

    private static TaskCondition OneOf(CrewTask decide, params string[] options) =>
        TaskCondition.Create(decide.Key!, [ConditionClause.OneOf(decide.Decision!, "verdict", options)]);

    private void Answers(params string[] sequence)
    {
        var calls = 0;
        _model.Respond = request =>
        {
            var choice = sequence[Math.Min(Interlocked.Increment(ref calls) - 1, sequence.Length - 1)];
            return new DecisionResponse
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
        };
    }

    private async Task<Orkeon.Domain.Crew.CrewOutput> RunAsync(GraphConfig graph, params CrewTask[] tasks)
    {
        var worker = new AgentBuilder().Role("Worker").Goal("Work").Backstory("Works").Build();
        var unitOfWork = new NullUnitOfWork();
        var taskRepository = new InMemoryTaskRepository(unitOfWork);
        var agentRepository = new InMemoryAgentRepository(unitOfWork);
        foreach (var task in tasks)
            await taskRepository.AddAsync(task);
        await agentRepository.AddAsync(worker);

        var builder = new CrewBuilder().Goal("Pipeline").Process(ProcessType.Graph).WithGraphConfig(graph).WithAgent(worker);
        foreach (var task in tasks)
            builder.WithTask(task);

        var runner = new DecisionTaskRunner(_model, DecisionCircuit.On, NullLogger<DecisionTaskRunner>.Instance);
        var dependencies = new CrewStrategyDependencies(taskRepository, agentRepository, _execution, _memoryScope)
        {
            DecisionRunner = runner,
        };
        DomainCrew crew = builder.Build();
        var strategy = new GraphProcessStrategy(
            dependencies,
            new AgentDelegationToolsProvider(new MockAgentCommunicationService(), _execution, NullLogger<AgentDelegationToolsProvider>.Instance),
            NullLogger<GraphProcessStrategy>.Instance,
            graphObserver: _observer);

        return await strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static GraphConfig Branching(CrewTask triage, int? maxVisits = null) => new()
    {
        Start = "triage",
        Edges =
        [
            new GraphEdgeConfig("triage", "fix", OneOf(triage, "approve")),
            new GraphEdgeConfig("triage", "escalate"),
            new GraphEdgeConfig("fix", GraphConfig.End),
            new GraphEdgeConfig("escalate", GraphConfig.End),
        ],
        MaxStateVisits = maxVisits,
    };

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldFollowTheEdgeWhoseWhenHolds()
    {
        Answers("approve");
        var triage = Decision("triage");

        var output = await RunAsync(Branching(triage), triage, Keyed("fix"), Keyed("escalate"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(["fix"], _ran);
        Assert.Equal(["triage", "fix"], _observer.Entered.Select(e => e.Node).ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldTakeTheOtherwiseEdge_WhenNoWhenHolds()
    {
        Answers("revise");
        var triage = Decision("triage");

        var output = await RunAsync(Branching(triage), triage, Keyed("fix"), Keyed("escalate"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(["escalate"], _ran);
        Assert.Equal(["triage", "escalate"], _observer.Entered.Select(e => e.Node).ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldFailAtBuild_WhenADecisionNodeHasNoOtherwiseEdge()
    {
        Answers("approve");
        var triage = Decision("triage");
        var graph = new GraphConfig
        {
            Start = "triage",
            Edges =
            [
                new GraphEdgeConfig("triage", "fix", OneOf(triage, "approve")),
                new GraphEdgeConfig("triage", "escalate", OneOf(triage, "revise")),
                new GraphEdgeConfig("fix", GraphConfig.End),
                new GraphEdgeConfig("escalate", GraphConfig.End),
            ],
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(graph, triage, Keyed("fix"), Keyed("escalate")));

        Assert.Contains("'triage'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("otherwise", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_ran);
        Assert.Empty(_observer.Entered);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRunALoop_TheLatestDecisionWinningOnARevisit()
    {
        Answers("revise", "revise", "approve");
        var review = Decision("review");
        var graph = new GraphConfig
        {
            Edges =
            [
                new GraphEdgeConfig("write", "review"),
                new GraphEdgeConfig("review", "write", OneOf(review, "revise")),
                new GraphEdgeConfig("review", "publish"),
                new GraphEdgeConfig("publish", GraphConfig.End),
            ],
        };

        var output = await RunAsync(graph, Keyed("write"), review, Keyed("publish"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(["write", "write", "write", "publish"], _ran);
        Assert.Equal(
            [("write", 1), ("review", 1), ("write", 2), ("review", 2), ("write", 3), ("review", 3), ("publish", 1)],
            _observer.Entered.Select(e => (e.Node, e.Visit)).ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldTripTheCircuitBreaker_WhenTheLoopNeverEnds()
    {
        Answers("revise");
        var review = Decision("review");
        var graph = new GraphConfig
        {
            MaxStateVisits = 3,
            Edges =
            [
                new GraphEdgeConfig("write", "review"),
                new GraphEdgeConfig("review", "write", OneOf(review, "revise")),
                new GraphEdgeConfig("review", "publish"),
                new GraphEdgeConfig("publish", GraphConfig.End),
            ],
        };

        var output = await RunAsync(graph, Keyed("write"), review, Keyed("publish"));

        Assert.False(output.Success);
        Assert.Contains("circuit breaker", output.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publish", _ran);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldStopTheRun_WhenANodeFails()
    {
        _failing.Add("first");

        var graph = new GraphConfig
        {
            Edges = [new GraphEdgeConfig("first", "second"), new GraphEdgeConfig("second", GraphConfig.End)],
        };

        var output = await RunAsync(graph, Keyed("first"), Keyed("second"));

        Assert.False(output.Success);
        Assert.Equal(["first"], _ran);
        Assert.Equal(["first"], _observer.Entered.Select(e => e.Node).ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldNameNodesByTheirIdAndRunTheirTask_WhenNodesAreWritten()
    {
        var graph = new GraphConfig
        {
            Nodes = [new GraphNodeConfig("draft", "write"), new GraphNodeConfig("ship", "publish")],
            Edges = [new GraphEdgeConfig("draft", "ship"), new GraphEdgeConfig("ship", GraphConfig.End)],
        };

        var output = await RunAsync(graph, Keyed("write"), Keyed("publish"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(["draft", "ship"], _observer.Entered.Select(e => e.Node).ToArray());
        Assert.Equal(["write", "publish"], _ran);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldStartAtTheNodeNamedByStart()
    {
        var graph = new GraphConfig
        {
            Start = "second",
            Edges = [new GraphEdgeConfig("second", "first"), new GraphEdgeConfig("first", GraphConfig.End)],
        };

        var output = await RunAsync(graph, Keyed("first"), Keyed("second"));

        Assert.True(output.Success, output.Error);
        Assert.Equal(["second", "first"], _ran);
    }
}
