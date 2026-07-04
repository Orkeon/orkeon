using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class SimilarityEvaluatorTests
{
    private readonly SimilarityEvaluatorTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnScoreOne_WhenOutputsAreIdentical()
    {
        var text = "The quick brown fox jumps over the lazy dog.";
        var input = new EvaluationInput(Output: text, ExpectedOutput: text);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreNearZero_WhenOutputsAreCompletelyDifferent()
    {
        var input = new EvaluationInput(
            Output: "alpha beta gamma delta epsilon",
            ExpectedOutput: "uno dos tres cuatro cinco seis siete");

        var result = await _fixture.EvaluateAsync(input);

        Assert.True(result.Score < 0.2, $"Expected score near 0.0 but got {result.Score}");
    }

    [Fact]
    public async Task ShouldReturnMiddleScore_WhenOutputsArePartiallySimilar()
    {
        var input = new EvaluationInput(
            Output: "The quick brown fox jumps over the lazy dog today.",
            ExpectedOutput: "The quick brown fox ran past the sleepy cat yesterday.");

        var result = await _fixture.EvaluateAsync(input);

        Assert.InRange(result.Score, 0.2, 0.9);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenNoExpectedOutput()
    {
        var input = new EvaluationInput(Output: "some output");

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
        Assert.Contains("No expected output", result.Reasoning);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenOutputIsEmpty()
    {
        var input = new EvaluationInput(Output: "", ExpectedOutput: "expected text");

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldContainSubMetrics_WhenAccessingDetails()
    {
        var input = new EvaluationInput(
            Output: "hello world",
            ExpectedOutput: "hello earth");

        var result = await _fixture.EvaluateAsync(input);

        Assert.NotNull(result.Details);
        Assert.True(result.Details.ContainsKey("levenshtein_similarity"));
        Assert.True(result.Details.ContainsKey("jaccard_similarity"));
        Assert.True(result.Details.ContainsKey("bigram_overlap"));
    }

    [Fact]
    public async Task ShouldNotRequireLlm_WhenAccessingRequiresLlm()
    {
        Assert.False(_fixture.GetEvaluator().RequiresLlm);
    }

    [Fact]
    public void ShouldReturnOne_WhenJaccardSetsAreIdentical()
    {
        var score = SimilarityEvaluatorTestsFixture.JaccardSimilarity("a b c", "a b c");
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void ShouldReturnZero_WhenJaccardSetsAreDisjoint()
    {
        var score = SimilarityEvaluatorTestsFixture.JaccardSimilarity("a b c", "x y z");
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void ShouldReturnOne_WhenBigramTextsAreIdentical()
    {
        var score = SimilarityEvaluatorTestsFixture.BigramOverlap("a b c d", "a b c d");
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void ShouldReturnOne_WhenLevenshteinStringsAreIdentical()
    {
        var score = SimilarityEvaluatorTestsFixture.NormalizedLevenshteinSimilarity("test", "test");
        Assert.Equal(1.0, score);
    }
}
