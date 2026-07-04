using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.LlmJudge;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Coverage for <see cref="GroundednessEvaluator"/> and the JSON / fallback parsing logic
/// it inherits from <c>LlmJudgeEvaluatorBase</c>.
/// </summary>
public class CovMisc_GroundednessEvaluatorTests
{
    [Fact]
    public void Constructor_NullChatClient_Throws()
        => Assert.Throws<ArgumentNullException>(() => new GroundednessEvaluator(null!));

    [Fact]
    public void Metadata_IsExposed()
    {
        using var chatClient = new MockChatClient();
        var evaluator = new GroundednessEvaluator(chatClient);

        Assert.Equal("Groundedness", evaluator.Name);
        Assert.True(evaluator.RequiresLlm);
        Assert.False(string.IsNullOrWhiteSpace(evaluator.Description));
    }

    [Fact]
    public async Task EvaluateAsync_WithContext_ParsesScoreFromJson()
    {
        using var mock = new MockChatClient();
        mock.SetGetResponseResult("""{"score": 8, "reasoning": "Well grounded."}""");
        var evaluator = new GroundednessEvaluator(mock);

        var input = new EvaluationInput(
            Output: "The Eiffel Tower is in Paris.",
            Context: "Paris is home to the Eiffel Tower.",
            TaskDescription: "Summarize the location.");

        var score = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal("Groundedness", score.EvaluatorName);
        Assert.Equal(0.8, score.Score, 3);
        Assert.Equal("Well grounded.", score.Reasoning);
        Assert.NotNull(score.Details);
        Assert.Equal(8.0, Assert.IsType<double>(score.Details!["raw_score"]));

        // Context branch: prompt should embed the supplied context and task description.
        var sentPrompt = mock.LastGetResponseMessages!.Last().Text;
        Assert.Contains("Paris is home to the Eiffel Tower", sentPrompt);
        Assert.Contains("Summarize the location", sentPrompt);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutContext_UsesNoContextNote()
    {
        using var mock = new MockChatClient();
        mock.SetGetResponseResult("""Here is my verdict: {"score": 5}""");
        var evaluator = new GroundednessEvaluator(mock);

        var input = new EvaluationInput(Output: "Some claim.");

        var score = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal(0.5, score.Score, 3);
        var sentPrompt = mock.LastGetResponseMessages!.Last().Text;
        Assert.Contains("No context was provided", sentPrompt);
    }

    [Fact]
    public async Task EvaluateAsync_NoScoreProperty_DefaultsToFive()
    {
        using var mock = new MockChatClient();
        mock.SetGetResponseResult("""{"reasoning": "no score field"}""");
        var evaluator = new GroundednessEvaluator(mock);

        var score = await evaluator.EvaluateAsync(new EvaluationInput("out"), TestContext.Current.CancellationToken);

        Assert.Equal(0.5, score.Score, 3); // 5.0 / 10 default
    }

    [Fact]
    public async Task EvaluateAsync_ScoreClampedToOne()
    {
        using var mock = new MockChatClient();
        mock.SetGetResponseResult("""{"score": 25}""");
        var evaluator = new GroundednessEvaluator(mock);

        var score = await evaluator.EvaluateAsync(new EvaluationInput("out"), TestContext.Current.CancellationToken);

        Assert.Equal(1.0, score.Score, 3);
    }

    [Fact]
    public async Task EvaluateAsync_NonJson_FallbackExtractsScoreOutOfTen()
    {
        using var mock = new MockChatClient();
        mock.SetGetResponseResult("I would rate this 7/10 overall.");
        var evaluator = new GroundednessEvaluator(mock);

        var score = await evaluator.EvaluateAsync(new EvaluationInput("out"), TestContext.Current.CancellationToken);

        Assert.Equal(0.7, score.Score, 3);
        Assert.NotNull(score.Details);
        Assert.True((bool)score.Details!["fallback_parse"]);
    }

    [Fact]
    public async Task EvaluateAsync_Unparseable_ReturnsNeutralHalf()
    {
        using var mock = new MockChatClient();
        mock.SetGetResponseResult("completely unstructured text with no number rating");
        var evaluator = new GroundednessEvaluator(mock);

        var score = await evaluator.EvaluateAsync(new EvaluationInput("out"), TestContext.Current.CancellationToken);

        Assert.Equal(0.5, score.Score, 3);
        Assert.NotNull(score.Details);
        Assert.True((bool)score.Details!["parse_failed"]);
    }

    [Fact]
    public async Task EvaluateAsync_ChatClientThrows_ReturnsZeroScore()
    {
        using var mock = new MockChatClient();
        mock.SetGetResponseException(new InvalidOperationException("judge offline"));
        var evaluator = new GroundednessEvaluator(mock);

        var score = await evaluator.EvaluateAsync(new EvaluationInput("out"), TestContext.Current.CancellationToken);

        Assert.Equal(0.0, score.Score);
        Assert.Contains("LLM judge failed", score.Reasoning);
    }
}
