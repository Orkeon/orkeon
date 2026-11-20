using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// #430: the <c>without:</c> relay of a decision task in an <c>optional</c> crew run with the circuit off - the
/// agent reports probabilities, the runner checks them and derives the same typed answers as the model path.
/// </summary>
public sealed class DecisionRelayRunnerTests
{
    private readonly FakeDecisionModel _model = new();
    private readonly FakeDecisionRelay _relay = new();

    private static readonly DomainAgent Analyst = new AgentBuilder()
        .Role(AgentRole.From("analyst"))
        .Goal(AgentGoal.From("Triage the reports"))
        .Build();

    private static TaskDecision Decision(DecisionQuestionSpec question, string? instructions = null) =>
        TaskDecision.Create([question], null, null, false, null).WithRelay(new DecisionRelay("analyst", instructions));

    private static ChoiceQuestionSpec Route() =>
        ChoiceQuestionSpec.Create("route", "Pick the route", new Dictionary<string, string> { ["bug"] = "a defect", ["other"] = "anything else" });

    private static ScoreQuestionSpec Severity() => ScoreQuestionSpec.Create("severity", "Rate it", ["low", "mid", "high"]);

    private static YesNoQuestionSpec Urgent() => YesNoQuestionSpec.Create("urgent", "The report is urgent.");

    private static CrewTask Task(TaskDecision decision) =>
        new CrewTaskBuilder().WithKey("triage").Description("Decide").ExpectedOutput("An outcome").WithDecision(decision).Build();

    private DecisionTaskRunner Runner(DecisionCircuit circuit) =>
        new(_model, _relay, circuit, NullLogger<DecisionTaskRunner>.Instance);

    private System.Threading.Tasks.Task<Orkeon.Application.Interfaces.Services.TaskResult> RunOffAsync(TaskDecision decision) =>
        Runner(DecisionCircuit.Off).RunAsync(Task(decision), [("report", "The build breaks on save.")], Analyst, TestContext.Current.CancellationToken);

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldChooseTheMostProbableOption_WhenTheRelayAnswersAChoice()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.7,"other":0.3}}"""];

        // Act
        var result = await RunOffAsync(Decision(Route()));

        // Assert
        Assert.True(result.Success, result.Error);
        var answer = result.Decision!.Answers["route"];
        Assert.Equal(("bug", 0.7), (answer.Chosen, Math.Round(answer.Confidence, 6)));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldReportTheRelayAsSourceAndNotDefaulted_WhenTheRelayAnswers()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.7,"other":0.3}}"""];

        // Act
        var result = await RunOffAsync(Decision(Route()));

        // Assert
        Assert.Equal((DecisionSources.Relay, false), (result.DecisionSource, result.Decision!.IsDefaulted));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldWeighTheLevelsFromZeroWithNoChosenLevel_WhenTheRelayAnswersAScore()
    {
        // Arrange
        _relay.Answers = ["""{"severity":{"0":0.2,"1":0.3,"2":0.5}}"""];

        // Act
        var result = await RunOffAsync(Decision(Severity()));

        // Assert
        var answer = result.Decision!.Answers["severity"];
        Assert.Equal((null, 1.3, 0.5), (answer.Chosen, Math.Round(answer.Score!.Value, 6), Math.Round(answer.Confidence, 6)));
    }

    [Theory]
    [InlineData(0.5, "yes", 0.5)]
    [InlineData(0.8, "yes", 0.8)]
    [InlineData(0.3, "no", 0.7)]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldAnswerYesFromOneHalf_WhenTheRelayAnswersAYesNo(double probability, string chosen, double confidence)
    {
        // Arrange
        _relay.Answers = [$$"""{"urgent":{{probability.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""];

        // Act
        var result = await RunOffAsync(Decision(Urgent()));

        // Assert
        var answer = result.Decision!.Answers["urgent"];
        Assert.Equal((chosen, confidence), (answer.Chosen, Math.Round(answer.Confidence, 6)));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldNormaliseTheDistribution_WhenItsSumIsWithinTheTolerance()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.6,"other":0.36}}"""];

        // Act
        var result = await RunOffAsync(Decision(Route()));

        // Assert
        Assert.Equal(1.0, Math.Round(result.Decision!.Answers["route"].Probabilities.Values.Sum(), 9));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldNeverCallTheRelay_WhenTheCircuitIsOn()
    {
        // Arrange
        var runner = Runner(DecisionCircuit.On);

        // Act
        var result = await runner.RunAsync(Task(Decision(Route())), [], Analyst, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((0, 1, DecisionSources.Model), (_relay.Requests.Count, _model.Requests.Count, result.DecisionSource));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"other_question":0.5}""")]
    [InlineData("""{"route":{"bug":1.4,"other":-0.4}}""")]
    [InlineData("""{"route":{"bug":0.5,"other":0.3}}""")]
    [InlineData("""{"route":{"bug":0.5}}""")]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldAskOnceMoreThenFailWithTheRelaySource_WhenTheAnswerFailsItsChecks(string answer)
    {
        // Arrange
        _relay.Answers = [answer];

        // Act
        var result = await RunOffAsync(Decision(Route()));

        // Assert
        Assert.Equal((false, 2, DecisionSources.Relay, (object?)null), (result.Success, _relay.Requests.Count, result.DecisionSource, (object?)result.Decision));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldTellTheAgentWhyItsFirstAnswerWasRefused_WhenItAsksAgain()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.5,"other":0.3}}""", """{"route":{"bug":0.6,"other":0.4}}"""];

        // Act
        await RunOffAsync(Decision(Route()));

        // Assert
        Assert.Contains("the probabilities of question 'route' sum to 0.8", _relay.Requests[1].Prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldSucceedAndCountBothCalls_WhenTheSecondAnswerPassesItsChecks()
    {
        // Arrange
        _relay.Answers = ["not json", """{"route":{"bug":0.6,"other":0.4}}"""];
        _relay.InputTokens = 100;
        _relay.OutputTokens = 20;

        // Act
        var result = await RunOffAsync(Decision(Route()));

        // Assert
        Assert.Equal((true, 200, 40, 240), (result.Success, result.PromptTokens, result.CompletionTokens, result.TokensUsed));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldFailWithTheRelaySourceAndNoFallback_WhenTheRelayThrows()
    {
        // Arrange
        _relay.Failure = new InvalidOperationException("provider down");
        var decision = TaskDecision.Create([Route()], null, null, false, new Dictionary<string, string> { ["route"] = "other" })
            .WithRelay(new DecisionRelay("analyst"));

        // Act
        var result = await RunOffAsync(decision);

        // Assert
        Assert.Equal(
            (false, DecisionSources.Relay, "The relay agent 'analyst' of decision task 'triage' failed: provider down"),
            (result.Success, result.DecisionSource, result.Error));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldFailNamingTheAgent_WhenTheCrewHasNoAgentForTheRelay()
    {
        // Arrange
        var runner = Runner(DecisionCircuit.Off);

        // Act
        var result = await runner.RunAsync(Task(Decision(Route())), [], null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Decision task 'triage' is answered by its without: relay (agent 'analyst') when the decision circuit is off: --no-decision, and the crew has no agent 'analyst'.",
            result.Error);
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldReportTheFallbackSource_WhenACrewWithNoModeRunsWithTheCircuitOff()
    {
        // Arrange
        var decision = TaskDecision.Create([Route()], null, null, false, new Dictionary<string, string> { ["route"] = "other" });

        // Act
        var result = await RunOffAsync(decision);

        // Assert
        Assert.Equal((DecisionSources.Fallback, true, 0), (result.DecisionSource, result.Decision!.IsDefaulted, _relay.Requests.Count));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldPassTheStateTheQuestionsAndTheInstructions_WhenAskingTheRelay()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.6,"other":0.4}}"""];

        // Act
        await RunOffAsync(Decision(Route(), "Count a crash as a bug."));

        // Assert
        var prompt = _relay.Requests[0].Prompt;
        Assert.All(
            ["The build breaks on save.", "\"route\" (a choice): Pick the route", "\"bug\": a defect", "Count a crash as a bug."],
            part => Assert.Contains(part, prompt, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldGiveTheAgentItsRoleGoalAndTheTaskAttribution_WhenAskingTheRelay()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.6,"other":0.4}}"""];
        var task = Task(Decision(Route()));

        // Act
        await Runner(DecisionCircuit.Off).RunAsync(task, [], Analyst, TestContext.Current.CancellationToken);

        // Assert
        var request = _relay.Requests[0];
        Assert.Equal((Analyst, task.Id.ToString(), true), (request.Agent, request.TaskId, request.SystemPrompt.StartsWith("You are analyst.\nYour goal: Triage the reports", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldGenerateAStrictSchemaFromTheQuestions_WhenAskingTheRelay()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.6,"other":0.4},"severity":{"0":0.2,"1":0.3,"2":0.5},"urgent":0.4}"""];
        var decision = TaskDecision.Create([Route(), Severity(), Urgent()], null, null, false, null).WithRelay(new DecisionRelay("analyst"));

        // Act
        await RunOffAsync(decision);

        // Assert
        using var schema = JsonDocument.Parse(_relay.Requests[0].Schema);
        var properties = schema.RootElement.GetProperty("properties");
        Assert.Equal(
            ("object", "number", "number", "number", false),
            (properties.GetProperty("route").GetProperty("type").GetString(),
             properties.GetProperty("severity").GetProperty("properties").GetProperty("2").GetProperty("type").GetString(),
             properties.GetProperty("urgent").GetProperty("type").GetString(),
             properties.GetProperty("route").GetProperty("properties").GetProperty("bug").GetProperty("type").GetString(),
             schema.RootElement.GetProperty("additionalProperties").GetBoolean()));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldWriteTheSchemaIntoThePromptWithSchema_WhenAskingTheRelay()
    {
        // Arrange
        _relay.Answers = ["""{"route":{"bug":0.6,"other":0.4}}"""];

        // Act
        await RunOffAsync(Decision(Route()));

        // Assert
        var request = _relay.Requests[0];
        Assert.Equal(
            (true, true),
            (request.PromptWithSchema.StartsWith(request.Prompt, StringComparison.Ordinal),
             request.PromptWithSchema.Contains(request.Schema, StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldReadTheAnswer_WhenTheAgentFencesItsJson()
    {
        // Arrange
        _relay.Answers = ["```json\n{\"route\":{\"bug\":0.2,\"other\":0.8}}\n```"];

        // Act
        var result = await RunOffAsync(Decision(Route()));

        // Assert
        Assert.Equal("other", result.Decision!.Answers["route"].Chosen);
    }

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldReportTheModelSource_WhenTheModelAnswersWithTheCircuitOn()
    {
        // Arrange
        var runner = new DecisionTaskRunner(_model, DecisionCircuit.On, NullLogger<DecisionTaskRunner>.Instance);

        // Act
        var result = await runner.RunAsync(Task(TaskDecision.Create([Route()], null, null, false, null)), [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionSources.Model, result.DecisionSource);
    }
}
