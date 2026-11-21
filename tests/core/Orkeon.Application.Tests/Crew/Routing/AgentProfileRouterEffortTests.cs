using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Routing;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew.Routing;

/// <summary>The thinking level System One picks for each task on <c>thinking: { effort: auto }</c> (#439).</summary>
public sealed class AgentProfileRouterEffortTests
{
    private const string Request = "Rename the variable x to count in parser.cs.";

    private readonly FakeDecisionModel _model = new();
    private readonly RecordingLogger _logger = new();

    private AgentProfileRouter Router(DecisionCircuit? circuit = null, string[]? levels = null, string[]? ladder = null)
    {
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(_model);
        if (circuit is not null)
            services.AddSingleton(circuit);
        return new AgentProfileRouter(
            services.BuildServiceProvider(),
            new AgentRouteOptions { Ladder = ladder ?? ["small", "large"] },
            new AgentEffortOptions { Levels = levels ?? [] },
            _logger);
    }

    private static DomainAgent Agent() => new AgentBuilder().Role("Coder").Goal("Change code").Build();

    private static ChoiceAnswer Choice(string choice, double probability) => new()
    {
        Choice = choice,
        Confidence = probability,
        Probabilities = ImmutableDictionary<string, double>.Empty.Add(choice, probability),
    };

    private void Answer(params (string Question, ChoiceAnswer Answer)[] answers) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = answers.ToImmutableDictionary(a => a.Question, a => (DecisionAnswer)a.Answer),
        };

    private static System.Threading.Tasks.Task<TaskRoute> EffortAsync(AgentProfileRouter router, bool routeProfile = false) =>
        router.RouteAsync(Agent(), "t1", Request, routeProfile, LlmAutoEffort.Any, null, TestContext.Current.CancellationToken);

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldRunWithThePickedLevel_WhenItsProbabilityReachesTheBar()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, Choice("high", 0.7)));

        // Act
        var route = await EffortAsync(Router());

        // Assert
        Assert.Null(route.Profile);
        Assert.Equal("high", route.Effort!.Level);
        Assert.Equal(AgentEffortReasons.Model, route.Effort.Reason);
        Assert.Equal(0.7, route.Effort.Probability);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldOfferTheHostLevelsCheapestFirst_WhenTheHostListsNone()
    {
        // Arrange
        var router = Router();

        // Act
        await EffortAsync(router);

        // Assert
        var question = Assert.IsType<ChoiceQuestion>(Assert.Single(Assert.Single(_model.Requests).Questions).Value);
        Assert.Equal(["high", "low", "medium"], question.Criteria.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(EffortPrompts.Lowest, question.Criteria["low"]!.GetValue<string>());
        Assert.Equal(EffortPrompts.Highest, question.Criteria["high"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldPickNoLevel_WhenThePicksProbabilityIsUnderTheBar()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, Choice("high", 0.59)));

        // Act
        var route = await EffortAsync(Router());

        // Assert
        Assert.Null(route.Effort!.Level);
        Assert.Equal(AgentEffortReasons.LowConfidence, route.Effort.Reason);
        Assert.Equal("high", route.Effort.Picked);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldPickNoLevel_WhenTheModelAnswersALevelNotOffered()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, Choice("max", 0.9)));

        // Act
        var route = await EffortAsync(Router());

        // Assert
        Assert.Null(route.Effort!.Level);
        Assert.Equal(AgentEffortReasons.NoAnswer, route.Effort.Reason);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldPickNoLevelAndWarn_WhenTheDecisionCallFails()
    {
        // Arrange
        _model.Failure = new InvalidOperationException("model down");

        // Act
        var route = await EffortAsync(Router());

        // Assert
        Assert.Equal(AgentEffortReasons.NoAnswer, route.Effort!.Reason);
        Assert.Contains("model down", Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldMakeNoCallAndWarnOnce_WhenTheCircuitIsOff()
    {
        // Arrange
        var router = Router(DecisionCircuit.Off);
        using var run = SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.Unspecified);
        await EffortAsync(router);

        // Act
        var route = await EffortAsync(router);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal(AgentEffortReasons.CircuitOff, route.Effort!.Reason);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldMakeNoCallAndNoWarning_WhenTheCrewIsOnDecisionModeNone()
    {
        // Arrange
        var router = Router(DecisionCircuit.Off);
        using var run = SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.None);

        // Act
        var route = await EffortAsync(router);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal(AgentEffortReasons.DecisionNone, route.Effort!.Reason);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldRunWithTheOnlyLevelAndMakeNoCall_WhenTheHostAllowsOneLevel()
    {
        // Arrange
        var router = Router(levels: ["medium"]);

        // Act
        var route = await EffortAsync(router);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal("medium", route.Effort!.Level);
        Assert.Equal(AgentEffortReasons.SingleLevel, route.Effort.Reason);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldAskTheProfileAndTheLevelInOneCall_WhenBothAreLeftToSystemOne()
    {
        // Arrange
        Answer((AgentProfileRouter.QuestionName, Choice("large", 0.8)), (AgentProfileRouter.EffortQuestionName, Choice("low", 0.4)));

        // Act
        var route = await EffortAsync(Router(), routeProfile: true);

        // Assert
        var request = Assert.Single(_model.Requests);
        Assert.Equal([AgentProfileRouter.EffortQuestionName, AgentProfileRouter.QuestionName], request.Questions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("large", route.Profile!.Profile);
        Assert.Equal(AgentEffortReasons.LowConfidence, route.Effort!.Reason);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldStillAskTheLevel_WhenTheHostDeclaresNoLadder()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, Choice("medium", 0.9)));

        // Act
        var route = await EffortAsync(Router(ladder: []), routeProfile: true);

        // Assert
        Assert.Equal(AgentRouteReasons.NoLadder, route.Profile!.Reason);
        Assert.Equal("medium", route.Effort!.Level);
    }
}
