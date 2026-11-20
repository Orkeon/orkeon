using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Routing;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Tests.Shared.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew.Routing;

/// <summary>The profile System One picks for each task of an agent on <c>llm.profile: auto</c> (#434).</summary>
public sealed class AgentProfileRouterTests
{
    private const string Request = "Rename the variable x to count in parser.cs.";

    private readonly FakeDecisionModel _model = new();
    private readonly RecordingLogger _logger = new();

    private AgentProfileRouter Router(DecisionCircuit? circuit = null, bool withModel = true, params string[] ladder)
    {
        var services = new ServiceCollection();
        if (withModel)
            services.AddSingleton<IDecisionModel>(_model);
        if (circuit is not null)
            services.AddSingleton(circuit);
        var options = new AgentRouteOptions { Ladder = ladder.Length == 0 ? ["small", "medium", "large"] : [.. ladder] };
        return new AgentProfileRouter(services.BuildServiceProvider(), options, _logger);
    }

    private static DomainAgent Agent() => new AgentBuilder().Role("Coder").Goal("Change code").Build();

    private void Answer(string choice, double confidence, params (string Option, double Share)[] shares) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add(AgentProfileRouter.QuestionName, new ChoiceAnswer
            {
                Choice = choice,
                Confidence = confidence,
                Probabilities = shares.ToImmutableDictionary(s => s.Option, s => s.Share),
            }),
        };

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldRunOnThePickedRung_WhenItsProbabilityReachesTheBar()
    {
        // Arrange
        Answer("small", 0.5, ("small", 0.6), ("large", 0.4));

        // Act
        var notice = await Router().RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("small", notice.Profile);
        Assert.Equal(AgentRouteReasons.Model, notice.Reason);
        Assert.Equal(0.6, notice.Probability);
        Assert.Equal("t1", notice.TaskId);
        Assert.Equal("Coder", notice.AgentRole);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldKeepTheDefaultProfile_WhenThePicksProbabilityIsUnderTheBar()
    {
        // Arrange
        Answer("large", 0.9, ("large", 0.59), ("small", 0.41));

        // Act
        var notice = await Router().RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LlmProfiles.Default, notice.Profile);
        Assert.Equal(AgentRouteReasons.LowConfidence, notice.Reason);
        Assert.Equal("large", notice.Picked);
        Assert.Equal(0.59, notice.Probability);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldReadTheAnswersConfidence_WhenItsProbabilitiesOmitThePick()
    {
        // Arrange
        Answer("medium", 0.8);

        // Act
        var notice = await Router().RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("medium", notice.Profile);
        Assert.Equal(0.8, notice.Probability);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldKeepTheDefaultProfile_WhenTheModelGivesNoAnswer()
    {
        // Arrange
        var router = Router();

        // Act
        var notice = await router.RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LlmProfiles.Default, notice.Profile);
        Assert.Equal(AgentRouteReasons.NoAnswer, notice.Reason);
        Assert.Null(notice.Picked);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldKeepTheDefaultProfile_WhenTheModelPicksAProfileOutOfTheLadder()
    {
        // Arrange
        Answer("huge", 0.99, ("huge", 0.99));

        // Act
        var notice = await Router().RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LlmProfiles.Default, notice.Profile);
        Assert.Equal(AgentRouteReasons.NoAnswer, notice.Reason);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldKeepTheDefaultProfileWithAWarning_WhenTheDecisionModelFails()
    {
        // Arrange
        _model.Failure = new InvalidOperationException("model down");

        // Act
        var notice = await Router().RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(LlmProfiles.Default, notice.Profile);
        Assert.Equal(AgentRouteReasons.NoAnswer, notice.Reason);
        Assert.Contains("model down", Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData(DecisionCircuitCause.Switch)]
    [InlineData(DecisionCircuitCause.Settings)]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCallAndKeepTheDefaultProfile_WhenTheCircuitIsOff(DecisionCircuitCause cause)
    {
        // Arrange
        Answer("large", 0.99, ("large", 0.99));
        var router = Router(DecisionCircuit.OffBy(cause));

        // Act
        var notice = await router.RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal(LlmProfiles.Default, notice.Profile);
        Assert.Equal(AgentRouteReasons.CircuitOff, notice.Reason);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldKeepTheDefaultProfile_WhenTheHostRegistersNoDecisionModel()
    {
        // Arrange
        var router = Router(withModel: false);

        // Act
        var notice = await router.RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentRouteReasons.CircuitOff, notice.Reason);
        Assert.Contains(
            DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!,
            Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnOnce_WhenTheCircuitIsOffForSeveralTasks()
    {
        // Arrange
        var router = Router(DecisionCircuit.Off);
        await router.RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Act
        await router.RouteAsync(Agent(), "t2", Request, null, TestContext.Current.CancellationToken);

        // Assert
        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(DecisionCircuit.Off.OffReason!, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldKeepTheDefaultProfileWithoutACall_WhenTheHostDeclaresNoLadder()
    {
        // Arrange
        var router = new AgentProfileRouter(
            new ServiceCollection().AddSingleton<IDecisionModel>(_model).BuildServiceProvider(), new AgentRouteOptions(), _logger);

        // Act
        var notice = await router.RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal(AgentRouteReasons.NoLadder, notice.Reason);
        Assert.Equal(LlmProfiles.Default, notice.Profile);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldOfferEachRungByItsPlaceOnTheLadder_WhenItAsksTheModel()
    {
        // Arrange
        var router = Router();

        // Act
        await router.RouteAsync(Agent(), "t1", Request, null, TestContext.Current.CancellationToken);

        // Assert
        var question = Assert.IsType<ChoiceQuestion>(Assert.Single(Assert.Single(_model.Requests).Questions).Value);
        Assert.Equal(["large", "medium", "small"], question.Criteria.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(RoutePrompts.Lowest, question.Criteria["small"]!.GetValue<string>());
        Assert.Equal(RoutePrompts.Middle, question.Criteria["medium"]!.GetValue<string>());
        Assert.Equal(RoutePrompts.Highest, question.Criteria["large"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldKeepTheStartAndTheEnd_WhenTheTaskIsLongerThanTheCut()
    {
        // Arrange
        var text = "START" + new string('x', 10_000) + "END";

        // Act
        var cut = AgentProfileRouter.MiddleCut(text, AgentProfileRouter.RequestChars);

        // Assert
        Assert.True(cut.Length <= AgentProfileRouter.RequestChars);
        Assert.StartsWith("START", cut, StringComparison.Ordinal);
        Assert.EndsWith("END", cut, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCall_WhenTheRunScopesCircuitIsOffAndTheContainersIsOn()
    {
        // Arrange
        Answer("large", 0.99, ("large", 0.99));
        var router = Router(DecisionCircuit.On);

        // Act
        var notice = await router.RouteAsync(Agent(), "t1", Request, DecisionCircuit.Off, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal(AgentRouteReasons.CircuitOff, notice.Reason);
    }
}
