using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.Task;

public class TaskConditionTests
{
    private static readonly TaskDecision Decision = TaskDecision.Create(
    [
        ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "A", ["b"] = "B", ["c"] = "C" }),
        ScoreQuestionSpec.Create("urgency", "Rate", ["low", "mid", "high"]),
        YesNoQuestionSpec.Create("legal", "Is it legal?"),
    ]);

    private static DecisionAnswerValue Intent(string chosen, double confidence, double a = 0.7, double b = 0.2, double c = 0.1) =>
        new()
        {
            Chosen = chosen,
            Confidence = confidence,
            Probabilities = new Dictionary<string, double> { ["a"] = a, ["b"] = b, ["c"] = c },
        };

    private static DecisionOutcome Outcome(params (string Question, DecisionAnswerValue Answer)[] answers) =>
        new() { Answers = answers.ToDictionary(x => x.Question, x => x.Answer) };

    private static TaskCondition OnIntent(params string[] options) =>
        TaskCondition.Create("decide", [ConditionClause.OneOf(Decision, "intent", options)]);

    [Fact]
    [Trait("BR", "BR-02")]
    [Trait("BR", "UC-02")]
    public void ShouldHold_WhenChosenOptionListedAndConfidenceAtFloor()
    {
        var outcome = Outcome(("intent", Intent("a", 0.5)));

        Assert.True(OnIntent("a", "b").Holds(outcome, Decision));
        Assert.False(OnIntent("b", "c").Holds(outcome, Decision));
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public void ShouldNotHold_WhenChosenOptionListedButConfidenceUnderFloor()
    {
        var outcome = Outcome(("intent", Intent("a", 0.49)));

        Assert.False(OnIntent("a").Holds(outcome, Decision));
    }

    [Fact]
    [Trait("BR", "BR-03")]
    public void ShouldHold_WhenOptionProbabilityMeetsThreshold_EvenIfNotChosen()
    {
        var outcome = Outcome(("intent", Intent("a", 0.8, b: 0.25)));

        var atLeast = TaskCondition.Create("decide", [ConditionClause.Threshold(Decision, "intent", "b", ConditionComparison.GreaterOrEqual, 0.2)]);
        var above = TaskCondition.Create("decide", [ConditionClause.Threshold(Decision, "intent", "b", ConditionComparison.GreaterOrEqual, 0.3)]);

        Assert.True(atLeast.Holds(outcome, Decision));
        Assert.False(above.Holds(outcome, Decision));
    }

    [Fact]
    [Trait("BR", "BR-03")]
    public void ShouldCompareWeightedScore_WhenThresholdOnScore()
    {
        var outcome = Outcome(("urgency", new DecisionAnswerValue { Score = 0.7, Confidence = 0.9 }));

        TaskCondition On(ConditionComparison comparison, double value) =>
            TaskCondition.Create("decide", [ConditionClause.Threshold(Decision, "urgency", null, comparison, value)]);

        Assert.True(On(ConditionComparison.GreaterOrEqual, 0.6).Holds(outcome, Decision));
        Assert.False(On(ConditionComparison.Greater, 0.8).Holds(outcome, Decision));
        Assert.True(On(ConditionComparison.Less, 0.8).Holds(outcome, Decision));
        Assert.False(On(ConditionComparison.LessOrEqual, 0.6).Holds(outcome, Decision));
    }

    [Fact]
    [Trait("BR", "BR-03")]
    public void ShouldCompareProbability_WhenThresholdOnYesNo()
    {
        var outcome = Outcome(("legal", new DecisionAnswerValue { Probability = 0.9, Confidence = 0.9 }));

        TaskCondition On(ConditionComparison comparison, double value) =>
            TaskCondition.Create("decide", [ConditionClause.Threshold(Decision, "legal", null, comparison, value)]);

        Assert.True(On(ConditionComparison.GreaterOrEqual, 0.8).Holds(outcome, Decision));
        Assert.False(On(ConditionComparison.LessOrEqual, 0.5).Holds(outcome, Decision));
    }

    [Fact]
    [Trait("BR", "UC-02")]
    public void ShouldRequireAllClauses_WhenSeveralGiven()
    {
        var condition = TaskCondition.Create("decide",
        [
            ConditionClause.OneOf(Decision, "intent", ["a"]),
            ConditionClause.Threshold(Decision, "legal", null, ConditionComparison.GreaterOrEqual, 0.8),
        ]);

        var both = Outcome(("intent", Intent("a", 0.9)), ("legal", new DecisionAnswerValue { Probability = 0.9, Confidence = 0.9 }));
        var onlyFirst = Outcome(("intent", Intent("a", 0.9)), ("legal", new DecisionAnswerValue { Probability = 0.1, Confidence = 0.9 }));

        Assert.True(condition.Holds(both, Decision));
        Assert.False(condition.Holds(onlyFirst, Decision));
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldHoldOtherwise_WhenNoSiblingHeldIncludingUnderFloor()
    {
        var siblings = new[] { OnIntent("a"), OnIntent("b") };
        var underFloor = Outcome(("intent", Intent("a", 0.3)));
        var unlisted = Outcome(("intent", Intent("c", 0.9)));

        var otherwise = TaskCondition.Otherwise("decide");

        Assert.True(otherwise.OtherwiseHolds(underFloor, Decision, siblings));
        Assert.True(otherwise.OtherwiseHolds(unlisted, Decision, siblings));
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldNotHoldOtherwise_WhenASiblingHeldOnAnswers()
    {
        var siblings = new[] { OnIntent("a"), OnIntent("b") };
        var outcome = Outcome(("intent", Intent("a", 0.9)));

        Assert.False(TaskCondition.Otherwise("decide").OtherwiseHolds(outcome, Decision, siblings));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseClause_WhenThresholdOutsideRangeOrUnknownQuestionOrOption()
    {
        Assert.ThrowsAny<ArgumentException>(() => ConditionClause.Threshold(Decision, "legal", null, ConditionComparison.Greater, 1.5));
        Assert.ThrowsAny<ArgumentException>(() => ConditionClause.Threshold(Decision, "intent", "b", ConditionComparison.Greater, -0.1));
        Assert.ThrowsAny<ArgumentException>(() => ConditionClause.Threshold(Decision, "ghost", null, ConditionComparison.Greater, 0.5));
        Assert.ThrowsAny<ArgumentException>(() => ConditionClause.Threshold(Decision, "intent", "zzz", ConditionComparison.Greater, 0.5));
        Assert.ThrowsAny<ArgumentException>(() => ConditionClause.OneOf(Decision, "ghost", ["a"]));
        Assert.ThrowsAny<ArgumentException>(() => ConditionClause.OneOf(Decision, "intent", ["zzz"]));
    }

    [Fact]
    [Trait("BR", "BR-03")]
    [Trait("BR", "BR-08")]
    [Trait("BR", "BR-12")]
    public void ShouldHoldOtherwise_WhenExplicitFallbackNamesAnotherOptionThanSiblingTests()
    {
        var route = TaskDecision.Create(
            [ChoiceQuestionSpec.Create("route", "Pick", new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" })],
            null,
            null,
            false,
            new Dictionary<string, string> { ["route"] = "a" });
        var sibling = TaskCondition.Create("decide", [ConditionClause.Threshold(route, "route", "b", ConditionComparison.Less, 0.3)]);
        var fallback = route.FallbackOutcome();

        Assert.True(sibling.Holds(fallback, route));
        Assert.False(TaskCondition.Otherwise("decide").OtherwiseHolds(fallback, route, [sibling]));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldBeEqual_WhenSameQuestionsFloorAndFallback()
    {
        var left = TaskCondition.Create("decide", [ConditionClause.OneOf(Decision, "intent", ["a", "b"])]);
        var right = TaskCondition.Create("decide", [ConditionClause.OneOf(Decision, "intent", ["a", "b"])]);
        var other = TaskCondition.Create("decide", [ConditionClause.OneOf(Decision, "intent", ["a", "c"])]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, other);
        Assert.Equal(left.Clauses[0], right.Clauses[0]);
        Assert.Equal(left.Clauses[0].GetHashCode(), right.Clauses[0].GetHashCode());
        Assert.NotEqual(left.Clauses[0], other.Clauses[0]);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldNotChange_WhenCallerMutatesCollectionsAfterCreate()
    {
        var options = new List<string> { "a" };
        var clause = ConditionClause.OneOf(Decision, "intent", options);
        var clauses = new List<ConditionClause> { clause };
        var condition = TaskCondition.Create("decide", clauses);

        options.Add("b");
        clauses.Add(ConditionClause.OneOf(Decision, "intent", ["c"]));

        Assert.Equal("a", Assert.Single(clause.Options));
        Assert.Single(condition.Clauses);
    }
}
