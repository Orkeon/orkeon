using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

public class CompositeScorerTests
{
    private readonly CompositeScorer _scorer;

    public CompositeScorerTests()
    {
        var options = Options.Create(new CognitiveMemoryOptions());
        _scorer = new CompositeScorer(options);
    }

    [Fact]
    public void Score_CalculatesCompositeCorrectly()
    {
        // Arrange
        var item = MemoryItem.Create("test content", importance: 0.8f);
        var results = new List<ScoredMemoryItem>
        {
            new(item, 0.9f) // semantic score 0.9
        };
        var options = new RecallOptions
        {
            SemanticWeight = 0.5f,
            RecencyWeight = 0.3f,
            ImportanceWeight = 0.2f,
            TopK = 10,
            MinScore = 0.0f
        };

        // Act
        var scored = _scorer.Score(results, options);

        // Assert
        Assert.Single(scored);
        var s = scored[0];
        Assert.Equal(0.9f, s.SemanticScore);
        Assert.Equal(0.8f, s.ImportanceScore);
        Assert.True(s.RecencyScore > 0.9f); // Very recent item should have high recency
        Assert.True(s.CompositeScore > 0);
    }

    [Fact]
    public void Score_RespectsWeights()
    {
        // Arrange - two items with different characteristics
        var recentImportant = MemoryItem.Create("important", importance: 1.0f);
        var oldTrivial = MemoryItem.Create("trivial", importance: 0.1f);

        var results = new List<ScoredMemoryItem>
        {
            new(recentImportant, 0.5f), // moderate semantic, high importance
            new(oldTrivial, 0.95f)      // high semantic, low importance
        };

        // Weight importance heavily
        var importanceHeavy = new RecallOptions
        {
            SemanticWeight = 0.1f,
            RecencyWeight = 0.1f,
            ImportanceWeight = 0.8f,
            TopK = 10,
            MinScore = 0.0f
        };

        // Act
        var scored = _scorer.Score(results, importanceHeavy);

        // Assert - important item should rank first when importance is heavily weighted
        Assert.Equal(2, scored.Count);
        Assert.Equal(1.0f, scored[0].ImportanceScore);
    }

    [Fact]
    public void Score_FiltersMinScore()
    {
        // Arrange
        var item = MemoryItem.Create("low relevance", importance: 0.05f);
        var results = new List<ScoredMemoryItem>
        {
            new(item, 0.05f) // very low semantic score
        };
        var options = new RecallOptions
        {
            MinScore = 0.5f, // High minimum
            TopK = 10
        };

        // Act
        var scored = _scorer.Score(results, options);

        // Assert - should be filtered out due to low composite score
        Assert.Empty(scored);
    }

    [Fact]
    public void Score_OrdersDescending()
    {
        // Arrange
        var items = new List<ScoredMemoryItem>
        {
            new(MemoryItem.Create("low", importance: 0.2f), 0.3f),
            new(MemoryItem.Create("high", importance: 0.9f), 0.9f),
            new(MemoryItem.Create("mid", importance: 0.5f), 0.6f)
        };
        var options = new RecallOptions { MinScore = 0.0f, TopK = 10 };

        // Act
        var scored = _scorer.Score(items, options);

        // Assert - should be in descending order of composite score
        Assert.Equal(3, scored.Count);
        Assert.True(scored[0].CompositeScore >= scored[1].CompositeScore);
        Assert.True(scored[1].CompositeScore >= scored[2].CompositeScore);
    }

    [Fact]
    public void Score_RespectsTopK()
    {
        // Arrange
        var items = new List<ScoredMemoryItem>();
        for (var i = 0; i < 20; i++)
        {
            items.Add(new ScoredMemoryItem(
                MemoryItem.Create($"item {i}", importance: 0.5f),
                0.5f));
        }
        var options = new RecallOptions { TopK = 5, MinScore = 0.0f };

        // Act
        var scored = _scorer.Score(items, options);

        // Assert
        Assert.Equal(5, scored.Count);
    }
}
