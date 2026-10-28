using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Tests.Crew;

public sealed class DecisionTaskRunnerTests
{
    private readonly FakeDecisionModel _model = new();

    public DecisionTaskRunnerTests()
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

    private static ChoiceQuestionSpec Choice(string name) =>
        ChoiceQuestionSpec.Create(name, "Pick " + name, new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" });

    private static CrewTask Decide(TaskDecision decision) =>
        new CrewTaskBuilder().Description("Decide").ExpectedOutput("An outcome").WithDecision(decision).Build();

    private static TaskDecision Of(
        int questions = 1,
        IReadOnlyList<string>? stateFrom = null,
        bool longContext = false,
        IReadOnlyDictionary<string, string>? fallback = null) =>
        TaskDecision.Create(
            Enumerable.Range(1, questions).Select(i => (DecisionQuestionSpec)Choice("q" + i)).ToList(),
            null,
            stateFrom,
            longContext,
            fallback);

    private DecisionTaskRunner Runner(DecisionCircuit circuit) =>
        new(_model, circuit, NullLogger<DecisionTaskRunner>.Instance);

    private static IReadOnlyList<(string TaskName, string Output)> NoInputs => [];

    [Fact]
    [Trait("BR", "BR-05")]
    [Trait("BR", "UC-01")]
    public async System.Threading.Tasks.Task ShouldSendAllQuestionsInOneRequest_WhenRunningDecisionTask()
    {
        var result = await Runner(DecisionCircuit.On).RunAsync(Decide(Of(questions: 3)), NoInputs, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var request = Assert.Single(_model.Requests);
        Assert.Equal(["q1", "q2", "q3"], request.Questions.Keys.Order().ToList());
    }

    [Fact]
    [Trait("BR", "UC-01")]
    public async System.Threading.Tasks.Task ShouldBuildStateFromDependencyOutputsInOrder_WhenNoStateDesignated()
    {
        var inputs = new[] { ("Alpha", "first-out"), ("Beta", "second-out") };

        await Runner(DecisionCircuit.On).RunAsync(Decide(Of()), inputs, CancellationToken.None);

        var state = Assert.Single(_model.Requests).State.ToJsonString();
        Assert.Contains("Alpha", state, StringComparison.Ordinal);
        Assert.Contains("Beta", state, StringComparison.Ordinal);
        Assert.Contains("first-out", state, StringComparison.Ordinal);
        Assert.True(state.IndexOf("first-out", StringComparison.Ordinal) < state.IndexOf("second-out", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "UC-01")]
    public async System.Threading.Tasks.Task ShouldBuildStateFromDesignatedTasksOnly_WhenStateDesignated()
    {
        var inputs = new[] { ("Alpha", "first-out"), ("Beta", "second-out") };

        await Runner(DecisionCircuit.On).RunAsync(Decide(Of(stateFrom: ["Beta"])), inputs, CancellationToken.None);

        var state = Assert.Single(_model.Requests).State.ToJsonString();
        Assert.Contains("second-out", state, StringComparison.Ordinal);
        Assert.DoesNotContain("first-out", state, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-01")]
    public async System.Threading.Tasks.Task ShouldReturnAnswersAsOutputAndOutcome_WhenModelAnswers()
    {
        var result = await Runner(DecisionCircuit.On).RunAsync(Decide(Of(questions: 2)), NoInputs, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Decision);
        Assert.Equal(result.Decision.ToCompactJson(), result.Output);
        Assert.Equal("a", result.Decision.Answers["q1"].Chosen);
        Assert.False(result.Decision.IsDefaulted);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.TryGetProperty("q1", out _));
        Assert.True(json.RootElement.TryGetProperty("q2", out _));
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public async System.Threading.Tasks.Task ShouldFailTaskWithReason_WhenModelCannotAnswer()
    {
        _model.Failure = new DecisionModelException("decision model unreachable: set Decision:ApiKey");

        var result = await Runner(DecisionCircuit.On).RunAsync(Decide(Of()), NoInputs, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("decision model unreachable: set Decision:ApiKey", result.Error);
        Assert.Null(result.Decision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "BR-09")]
    public async System.Threading.Tasks.Task ShouldAskLongContext_OnlyWhenTaskAsksForIt(bool longContext)
    {
        await Runner(DecisionCircuit.On).RunAsync(Decide(Of(longContext: longContext)), NoInputs, CancellationToken.None);

        Assert.Equal(longContext, Assert.Single(_model.Requests).LongContext);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    [Trait("BR", "UC-04")]
    public async System.Threading.Tasks.Task ShouldAnswerByFallbackWithoutRequest_WhenCircuitOff()
    {
        var result = await Runner(DecisionCircuit.Off).RunAsync(Decide(Of()), NoInputs, CancellationToken.None);

        Assert.Empty(_model.Requests);
        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Decision);
        Assert.True(result.Decision.IsDefaulted);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public async System.Threading.Tasks.Task ShouldMarkDefaultedWithFullConfidence_WhenExplicitFallbackAnswers()
    {
        var decision = Of(fallback: new Dictionary<string, string> { ["q1"] = "b" });

        var result = await Runner(DecisionCircuit.Off).RunAsync(Decide(decision), NoInputs, CancellationToken.None);

        Assert.NotNull(result.Decision);
        Assert.True(result.Decision.IsDefaulted);
        Assert.Equal("b", result.Decision.Answers["q1"].Chosen);
        Assert.Equal(1, result.Decision.Answers["q1"].Confidence);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public async System.Threading.Tasks.Task ShouldMarkDefaultedWithNoOption_WhenDefaultFallback()
    {
        var result = await Runner(DecisionCircuit.Off).RunAsync(Decide(Of()), NoInputs, CancellationToken.None);

        Assert.NotNull(result.Decision);
        Assert.True(result.Decision.IsDefaulted);
        Assert.Null(result.Decision.Answers["q1"].Chosen);
        Assert.Equal(0, result.Decision.Answers["q1"].Confidence);
    }

    [Fact]
    [Trait("BR", "BR-12")]
    [Trait("BR", "BR-06")]
    public async System.Threading.Tasks.Task ShouldFailTaskNotDefault_WhenCircuitOnAndModelFails()
    {
        _model.Failure = new DecisionModelException("rate limited");
        var decision = Of(fallback: new Dictionary<string, string> { ["q1"] = "b" });

        var result = await Runner(DecisionCircuit.On).RunAsync(Decide(decision), NoInputs, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("rate limited", result.Error);
        Assert.Null(result.Decision);
    }
}
