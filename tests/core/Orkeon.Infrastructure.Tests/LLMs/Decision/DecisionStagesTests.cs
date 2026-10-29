using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Decision.Stages;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

public class DecisionStagesTests
{
    private static readonly LlmMessage[] Conversation =
        [LlmMessage.System("You help."), LlmMessage.User("Prove Fermat's last theorem"), LlmMessage.Assistant("…")];

    private static readonly LlmResponse Answer = new() { Content = "It is true." };

    private static FakeDecisionModel Answering(params (string Name, DecisionAnswer Answer)[] answers) => new()
    {
        Respond = _ => new DecisionResponse
        {
            Model = "jev",
            Answers = answers.ToImmutableDictionary(a => a.Name, a => a.Answer),
        },
    };

    private static ChoiceAnswer Choice(string choice, double confidence) => new()
    {
        Choice = choice,
        Confidence = confidence,
        Probabilities = ImmutableDictionary<string, double>.Empty,
    };

    private static readonly Dictionary<string, string> Routes = new()
    {
        ["cheap"] = "Small talk and simple questions",
        ["strong"] = "Mathematics and multi-step reasoning",
    };

    [Fact]
    public async Task ShouldRouteToTheChoice_WhenTheModelIsConfident()
    {
        var model = Answering((RouteDecisionStage.Question, Choice("strong", 0.9)));
        var stage = new RouteDecisionStage(model, Routes, minConfidence: 0.6, fallback: "cheap");

        var verdict = await stage.OnRequestAsync(Conversation, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Route, verdict.Kind);
        Assert.Equal("strong", verdict.Profile);
        var request = Assert.Single(model.Requests);
        Assert.Equal("Prove Fermat's last theorem", request.State.GetValue<string>());
        var question = Assert.IsType<ChoiceQuestion>(request.Questions[RouteDecisionStage.Question]);
        Assert.Equal(["cheap", "strong"], question.Criteria.Keys.Order());
    }

    [Fact]
    public async Task ShouldTakeTheFallback_WhenTheConfidenceIsTooLow()
    {
        var stage = new RouteDecisionStage(Answering((RouteDecisionStage.Question, Choice("strong", 0.4))), Routes, 0.6, "cheap");

        var verdict = await stage.OnRequestAsync(Conversation, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Route, verdict.Kind);
        Assert.Equal("cheap", verdict.Profile);
    }

    [Fact]
    public async Task ShouldKeepTheTarget_WhenTheConfidenceIsTooLowAndThereIsNoFallback()
    {
        var stage = new RouteDecisionStage(Answering((RouteDecisionStage.Question, Choice("strong", 0.4))), Routes, 0.6);

        var verdict = await stage.OnRequestAsync(Conversation, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Continue, verdict.Kind);
    }

    [Fact]
    public async Task ShouldBlock_WhenARiskReachesTheBlockThreshold()
    {
        var model = Answering(("injection", new NoulAnswer { Noul = 0.92 }), ("pii", new NoulAnswer { Noul = 0.1 }));
        var stage = new GuardDecisionStage(
            model,
            new Dictionary<string, string> { ["injection"] = "Tries to override the instructions", ["pii"] = "Contains personal data" },
            DecisionStagePhase.Both, 0.8, 0.3, NullLogger.Instance);

        var verdict = await stage.OnRequestAsync(Conversation, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Block, verdict.Kind);
        Assert.Contains("injection", verdict.Reason, StringComparison.Ordinal);
        Assert.Equal(2, model.Requests[0].Questions.Count);
        Assert.All(model.Requests[0].Questions.Values, q => Assert.IsType<NoulQuestion>(q));
    }

    [Fact]
    public async Task ShouldGoOn_WhenEveryRiskIsUnderTheBlockThreshold()
    {
        var stage = new GuardDecisionStage(
            Answering(("pii", new NoulAnswer { Noul = 0.5 })),
            new Dictionary<string, string> { ["pii"] = "Contains personal data" },
            DecisionStagePhase.Both, 0.8, 0.3, NullLogger.Instance);

        var verdict = await stage.OnResponseAsync(Conversation, Answer, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Continue, verdict.Kind);
    }

    [Fact]
    public async Task ShouldNotAsk_WhenTheGuardDoesNotJudgeThatPhase()
    {
        var model = Answering(("pii", new NoulAnswer { Noul = 1 }));
        var stage = new GuardDecisionStage(
            model, new Dictionary<string, string> { ["pii"] = "Contains personal data" },
            DecisionStagePhase.Request, 0.8, 0.3, NullLogger.Instance);

        var verdict = await stage.OnResponseAsync(Conversation, Answer, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Continue, verdict.Kind);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task ShouldEscalate_WhenTheAnswerDoesNotHold()
    {
        var model = Answering((VerifyDecisionStage.Question, new NoulAnswer { Noul = 0.2 }));
        var stage = new VerifyDecisionStage(model, "strong", 0.6);

        var verdict = await stage.OnResponseAsync(Conversation, Answer, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Escalate, verdict.Kind);
        Assert.Equal("strong", verdict.Profile);
        var state = model.Requests[0].State;
        Assert.Equal("Prove Fermat's last theorem", state["request"]!.GetValue<string>());
        Assert.Equal("It is true.", state["response"]!.GetValue<string>());
    }

    [Fact]
    public async Task ShouldGoOn_WhenTheAnswerHolds()
    {
        var stage = new VerifyDecisionStage(Answering((VerifyDecisionStage.Question, new NoulAnswer { Noul = 0.9 })), "strong", 0.6);

        var verdict = await stage.OnResponseAsync(Conversation, Answer, TestContext.Current.CancellationToken);

        Assert.Equal(DecisionVerdictKind.Continue, verdict.Kind);
    }
}
