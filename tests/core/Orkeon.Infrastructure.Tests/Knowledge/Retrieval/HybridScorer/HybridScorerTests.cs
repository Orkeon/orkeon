namespace Orkeon.Infrastructure.Tests.Knowledge.Retrieval;

public class HybridScorerTests
{
    private readonly HybridScorerTestsFixture _fixture = new();

    [Fact]
    public void ShouldCalculateCorrectly_WhenComputingRRFScoreWithEqualRanks()
    {
        var score = HybridScorerTestsFixture.ComputeRRFScore(1, 1);
        var expected = 2f / 61f;

        Assert.Equal(expected, score, 0.0001f);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenComputingRRFScoreWithDifferentRanks()
    {
        var score = HybridScorerTestsFixture.ComputeRRFScore(1, 10);
        var expected = (1f / 61f) + (1f / 70f);

        Assert.Equal(expected, score, 0.0001f);
    }

    [Fact]
    public void ShouldApplyCorrectWeighting_WhenComputingWeightedScore()
    {
        var score = HybridScorerTestsFixture.ComputeWeightedScore(0.8f, 0.6f, 0.7f, 0.3f);
        var expected = 0.8f * 0.7f + 0.6f * 0.3f;

        Assert.Equal(expected, score, 0.0001f);
    }

    [Fact]
    public void ShouldApplyEqualWeighting_WhenWeightsAreEqual()
    {
        var score = HybridScorerTestsFixture.ComputeWeightedScore(0.9f, 0.3f, 0.5f, 0.5f);
        var expected = 0.9f * 0.5f + 0.3f * 0.5f;

        Assert.Equal(expected, score, 0.0001f);
    }

    [Fact]
    public void ShouldReturnOne_WhenAllKeywordTermsMatch()
    {
        var score = HybridScorerTestsFixture.ComputeKeywordScore(
            "machine learning model",
            "This is a machine learning model for prediction.");

        Assert.Equal(1.0f, score, 0.01f);
    }

    [Fact]
    public void ShouldReturnZero_WhenNoKeywordTermsMatch()
    {
        var score = HybridScorerTestsFixture.ComputeKeywordScore(
            "quantum computing",
            "This is about cooking recipes.");

        Assert.Equal(0.0f, score, 0.01f);
    }

    [Fact]
    public void ShouldReturnMatchRatio_WhenKeywordTermsPartiallyMatch()
    {
        var score = HybridScorerTestsFixture.ComputeKeywordScore(
            "machine learning prediction",
            "This is a machine learning tutorial.");

        Assert.Equal(2f / 3f, score, 0.01f);
    }

    [Fact]
    public void ShouldReturnZero_WhenKeywordQueryIsEmpty()
    {
        var score = HybridScorerTestsFixture.ComputeKeywordScore("", "Some document text.");

        Assert.Equal(0.0f, score, 0.01f);
    }

    [Fact]
    public void ShouldMatchCaseInsensitively_WhenComputingKeywordScore()
    {
        var score = HybridScorerTestsFixture.ComputeKeywordScore(
            "MACHINE LEARNING",
            "machine learning is great");

        Assert.Equal(1.0f, score, 0.01f);
    }
}
