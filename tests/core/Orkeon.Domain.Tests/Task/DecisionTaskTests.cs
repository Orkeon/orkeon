using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.Task;

public class DecisionTaskTests
{
    private static ChoiceQuestionSpec Intent(params string[] options) =>
        ChoiceQuestionSpec.Create("intent", "Pick the intent", options.ToDictionary(o => o, o => o + " option"));

    private static TaskDecision OneQuestion(
        IReadOnlyDictionary<string, string>? fallback = null,
        double? floor = null) =>
        TaskDecision.Create([Intent("a", "b")], floor, null, false, fallback);

    private static CrewTaskBuilder Minimal() =>
        new CrewTaskBuilder().Description("Decide").ExpectedOutput("An outcome");

    private static TaskConfiguration Task(string key, TaskDecision? decision = null, TaskCondition? condition = null, bool agent = false) =>
        new()
        {
            Key = key,
            Description = key,
            ExpectedOutput = key,
            Decision = decision,
            Condition = condition,
            AssignedAgentId = agent ? AgentId.Create() : null,
        };

    private static IReadOnlyList<DecisionTaskDeclaration> Declarations(params TaskConfiguration[] tasks) =>
        new CrewConfiguration { Tasks = tasks }.DecisionDeclarations();

    [Fact]
    [Trait("BR", "BR-01")]
    [Trait("BR", "UC-01")]
    public void ShouldBuildDecisionTask_WhenDecisionGivenAndNoAgent()
    {
        var decision = OneQuestion();

        var task = Minimal().WithDecision(decision).Build();

        Assert.True(task.IsDecision);
        Assert.Same(decision, task.Decision);
        Assert.Null(task.AssignedAgent);
    }

    [Fact]
    [Trait("BR", "BR-01")]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseTask_WhenAgentAndDecisionBothGiven()
    {
        var builder = Minimal().AssignTo(AgentId.Create()).WithDecision(OneQuestion());

        Assert.Throws<BuilderValidationException>(() => builder.Build());
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseDecision_WhenNoQuestion()
    {
        Assert.ThrowsAny<ArgumentException>(() => TaskDecision.Create([]));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseDecision_WhenOver255OptionsOrOver10Levels()
    {
        var tooManyOptions = Enumerable.Range(0, 256).ToDictionary(i => "o" + i, i => "d");
        var tooManyLevels = Enumerable.Range(0, 11).Select(i => "l" + i).ToList();

        Assert.ThrowsAny<ArgumentException>(() => ChoiceQuestionSpec.Create("q", "i", tooManyOptions));
        Assert.ThrowsAny<ArgumentException>(() => ScoreQuestionSpec.Create("q", "i", tooManyLevels));
        Assert.Equal(255, ChoiceQuestionSpec.Create("q", "i", tooManyOptions.Take(255).ToDictionary()).Options.Count);
        Assert.Equal(10, ScoreQuestionSpec.Create("q", "i", tooManyLevels.Take(10).ToList()).Levels.Count);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseDecision_WhenFloorOutsideUnitRange()
    {
        Assert.ThrowsAny<ArgumentException>(() => OneQuestion(floor: -0.1));
        Assert.ThrowsAny<ArgumentException>(() => OneQuestion(floor: 1.1));
        Assert.Equal(0.0, OneQuestion(floor: 0.0).ConfidenceFloor);
        Assert.Equal(1.0, OneQuestion(floor: 1.0).ConfidenceFloor);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public void ShouldDefaultFloorToHalf_WhenNotGiven()
    {
        Assert.Equal(0.5, OneQuestion().ConfidenceFloor);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseCrew_WhenConditionNamesUnknownOrNonDecisionTask()
    {
        var decision = OneQuestion();
        var other = TaskDecision.Create([Intent("a", "b", "z"), YesNoQuestionSpec.Create("extra", "Is it?")]);
        var declarations = Declarations(
            Task("classify", decision),
            Task("plain", agent: true),
            Task("c1", condition: TaskCondition.Create("ghost", [ConditionClause.OneOf(decision, "intent", ["a"])]), agent: true),
            Task("c2", condition: TaskCondition.Create("plain", [ConditionClause.OneOf(decision, "intent", ["a"])]), agent: true),
            Task("c3", condition: TaskCondition.Create("classify", [ConditionClause.Threshold(other, "extra", null, ConditionComparison.Greater, 0.5)]), agent: true),
            Task("c4", condition: TaskCondition.Create("classify", [ConditionClause.OneOf(other, "intent", ["z"])]), agent: true));

        var messages = DecisionTaskRules.Check(ProcessType.Sequential, declarations);

        Assert.Equal(4, messages.Count);
        foreach (var key in new[] { "c1", "c2", "c3", "c4" })
        {
            Assert.Single(messages, m => m.Contains(key));
        }
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "BR-08")]
    public void ShouldRefuseCrew_WhenOtherwiseHasNoSiblingOrIsDuplicated()
    {
        var decision = OneQuestion();
        var lonely = Declarations(
            Task("classify", decision),
            Task("o1", condition: TaskCondition.Otherwise("classify"), agent: true));
        var duplicated = Declarations(
            Task("classify", decision),
            Task("c1", condition: TaskCondition.Create("classify", [ConditionClause.OneOf(decision, "intent", ["a"])]), agent: true),
            Task("o1", condition: TaskCondition.Otherwise("classify"), agent: true),
            Task("o2", condition: TaskCondition.Otherwise("classify"), agent: true));

        var lonelyMessages = DecisionTaskRules.Check(ProcessType.Sequential, lonely);
        var duplicatedMessages = DecisionTaskRules.Check(ProcessType.Sequential, duplicated);

        Assert.Contains("o1", Assert.Single(lonelyMessages));
        Assert.Contains("o2", Assert.Single(duplicatedMessages));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseCrew_WhenProcessIsNotSequentialParallelOrGraph()
    {
        var decision = OneQuestion();
        var declarations = Declarations(
            Task("classify", decision),
            Task("c1", condition: TaskCondition.Create("classify", [ConditionClause.OneOf(decision, "intent", ["a"])]), agent: true));

        var refused = DecisionTaskRules.Check(ProcessType.Hierarchical, declarations);

        Assert.Equal(2, refused.Count);
        Assert.Single(refused, m => m.Contains("classify"));
        Assert.Single(refused, m => m.Contains("c1"));
        Assert.Empty(DecisionTaskRules.Check(ProcessType.Sequential, declarations));
        Assert.Empty(DecisionTaskRules.Check(ProcessType.Parallel, declarations));
        Assert.Empty(DecisionTaskRules.Check(ProcessType.Graph, declarations));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "UC-04")]
    public void ShouldRefuseFallback_WhenItNamesUnknownQuestionOrOption()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            OneQuestion(new Dictionary<string, string> { ["ghost"] = "a" }));
        Assert.ThrowsAny<ArgumentException>(() =>
            OneQuestion(new Dictionary<string, string> { ["intent"] = "zzz" }));
        Assert.True(OneQuestion(new Dictionary<string, string> { ["intent"] = "a" }).HasExplicitFallback);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "UC-04")]
    public void ShouldListDecisionsWithoutFallback_WhenNoExplicitAnswersNorOtherwiseSibling()
    {
        var withFallback = OneQuestion(new Dictionary<string, string> { ["intent"] = "a" });
        var plain = OneQuestion();
        var declarations = Declarations(
            Task("d1", withFallback),
            Task("d2", plain),
            Task("d3", plain),
            Task("o2", condition: TaskCondition.Otherwise("d2"), agent: true));

        var messages = DecisionTaskRules.CheckFallbacks(declarations);

        Assert.Contains("d3", Assert.Single(messages));
    }

    [Fact]
    [Trait("BR", "BR-01")]
    public void ShouldRefuseAssignTo_WhenTaskIsDecision()
    {
        var task = Minimal().WithDecision(OneQuestion()).Build();

        Assert.ThrowsAny<InvalidOperationException>(() => task.AssignTo(AgentId.Create()));
        Assert.Null(task.AssignedAgent);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseDecision_WhenQuestionSpecBuiltAroundCreateExceedsBounds()
    {
        var choice = new ChoiceQuestionSpec
        {
            Name = "q",
            Options = Enumerable.Range(0, 256).ToDictionary(i => "o" + i, i => "d"),
        };
        var score = new ScoreQuestionSpec
        {
            Name = "s",
            Levels = Enumerable.Range(0, 11).Select(i => "l" + i).ToList(),
        };

        Assert.ThrowsAny<ArgumentException>(() => TaskDecision.Create([choice]));
        Assert.ThrowsAny<ArgumentException>(() => TaskDecision.Create([score]));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldBeEqual_WhenSameQuestionsFloorAndFallback()
    {
        var question = Intent("a", "b");
        var left = TaskDecision.Create([question], 0.6, ["prev"], false, new Dictionary<string, string> { ["intent"] = "a" });
        var right = TaskDecision.Create([question], 0.6, ["prev"], false, new Dictionary<string, string> { ["intent"] = "a" });
        var other = TaskDecision.Create([question], 0.7, ["prev"], false, new Dictionary<string, string> { ["intent"] = "a" });

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, other);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldNotChange_WhenCallerMutatesCollectionsAfterCreate()
    {
        var question = Intent("a", "b");
        var questions = new List<DecisionQuestionSpec> { question };
        var fallback = new Dictionary<string, string> { ["intent"] = "a" };
        var stateFrom = new List<string> { "prev" };
        var decision = TaskDecision.Create(questions, null, stateFrom, false, fallback);

        questions.Add(YesNoQuestionSpec.Create("extra", "Is it?"));
        fallback["intent"] = "b";
        fallback["extra"] = "yes";
        stateFrom.Add("later");

        Assert.Single(decision.Questions);
        Assert.Equal("a", Assert.Single(decision.Fallback).Value);
        Assert.Equal("prev", Assert.Single(decision.StateFrom));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldNotChange_WhenCallerMutatesSpecCollectionsAfterCreate()
    {
        var options = new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" };
        var levels = new List<string> { "low", "mid", "high" };
        var decision = TaskDecision.Create(
        [
            new ChoiceQuestionSpec { Name = "r", Options = options },
            new ScoreQuestionSpec { Name = "s", Levels = levels },
        ]);

        options.Clear();
        levels.Clear();

        Assert.Equal(2, Assert.IsType<ChoiceQuestionSpec>(decision.Question("r")).Options.Count);
        Assert.Equal(3, Assert.IsType<ScoreQuestionSpec>(decision.Question("s")).Levels.Count);
    }
}
