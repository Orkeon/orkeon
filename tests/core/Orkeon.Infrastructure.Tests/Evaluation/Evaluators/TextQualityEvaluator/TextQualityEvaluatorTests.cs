using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class TextQualityEvaluatorTests
{
    private readonly TextQualityEvaluatorTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnHighScore_WhenTextQualityIsGood()
    {
        var text = "The artificial intelligence industry has undergone remarkable transformation in recent years. " +
                   "Machine learning algorithms now power applications ranging from medical diagnostics to autonomous vehicles. " +
                   "Natural language processing enables computers to understand human communication with unprecedented accuracy. " +
                   "Meanwhile, computer vision systems can identify objects and patterns in images with superhuman precision. " +
                   "These advances have created both opportunities and challenges for society at large. " +
                   "Ethical considerations remain paramount as organizations deploy increasingly powerful AI systems. " +
                   "Researchers continue to explore novel architectures and training methodologies. " +
                   "The convergence of hardware improvements and algorithmic breakthroughs promises further innovation.";

        var input = new EvaluationInput(Output: text);

        var result = await _fixture.EvaluateAsync(input);

        Assert.True(result.Score > 0.5, $"Expected score > 0.5 but got {result.Score}");
    }

    [Fact]
    public async Task ShouldReturnLowerScore_WhenTextIsVeryShort()
    {
        var input = new EvaluationInput(Output: "Too short.");

        var result = await _fixture.EvaluateAsync(input);

        Assert.True(result.Score < 0.8, $"Expected score < 0.8 for very short text but got {result.Score}");
    }

    [Fact]
    public async Task ShouldReturnLowerScore_WhenTextIsRepetitive()
    {
        var text = string.Join(" ",
            Enumerable.Repeat("The cat sat on the mat. The cat sat on the mat. The cat sat on the mat.", 5));

        var input = new EvaluationInput(Output: text);

        var result = await _fixture.EvaluateAsync(input);

        Assert.True(result.Score < 0.8, $"Expected score < 0.8 for repetitive text but got {result.Score}");
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenTextIsEmpty()
    {
        var input = new EvaluationInput(Output: "");

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenTextIsWhitespaceOnly()
    {
        var input = new EvaluationInput(Output: "   \n\t  ");

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldClampScoreBetweenZeroAndOne_WhenEvaluating()
    {
        var input = new EvaluationInput(
            Output: "Some text for evaluation purposes that has enough words to be meaningful.");

        var result = await _fixture.EvaluateAsync(input);

        Assert.InRange(result.Score, 0.0, 1.0);
    }

    [Fact]
    public async Task ShouldContainSubMetrics_WhenAccessingDetails()
    {
        var input = new EvaluationInput(
            Output: "This is a test. It has multiple sentences. The text should be evaluated for quality.");

        var result = await _fixture.EvaluateAsync(input);

        Assert.NotNull(result.Details);
        Assert.True(result.Details.ContainsKey("word_count"));
        Assert.True(result.Details.ContainsKey("sentence_count"));
        Assert.True(result.Details.ContainsKey("vocabulary_richness_score"));
        Assert.True(result.Details.ContainsKey("repetition_score"));
    }

    [Fact]
    public async Task ShouldNotRequireLlm_WhenAccessingRequiresLlm()
    {
        Assert.False(_fixture.GetEvaluator().RequiresLlm);
    }
}
