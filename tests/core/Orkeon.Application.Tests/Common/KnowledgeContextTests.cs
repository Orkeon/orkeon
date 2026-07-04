using Orkeon.Application.Rag;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Application.Tests.Common;

public class KnowledgeContextTests
{
    [Fact]
    public void ShouldCreateEmptyContext_WhenConstructing()
    {
        // Act
        var context = new KnowledgeContext();

        // Assert
        Assert.NotNull(context);
        Assert.Empty(context.RelevantKnowledge);
        Assert.Empty(context.Metadata);
    }

    [Fact]
    public void ShouldAddToContext_WhenAddingRelevantKnowledgeWithSingleItem()
    {
        // Arrange
        var context = new KnowledgeContext();
        var item = KnowledgeItem.Create(TestContent, "test-source");

        // Act
        context.AddRelevantKnowledge(item);

        // Assert
        Assert.Single(context.RelevantKnowledge);
        Assert.Equal(item, context.RelevantKnowledge[0]);
    }

    [Fact]
    public void ShouldAddAllToContext_WhenAddingRelevantKnowledgeWithMultipleItems()
    {
        // Arrange
        var context = new KnowledgeContext();
        var items = new[]
        {
            KnowledgeItem.Create("Content 1", "source-1"),
            KnowledgeItem.Create("Content 2", "source-2"),
            KnowledgeItem.Create("Content 3", "source-3")
        };

        // Act
        context.AddRelevantKnowledge(items);

        // Assert
        Assert.Equal(3, context.RelevantKnowledge.Count);
        Assert.Equal(items, context.RelevantKnowledge);
    }

    [Fact]
    public void ShouldAccumulateItems_WhenAddingRelevantKnowledgeWithMultipleCalls()
    {
        // Arrange
        var context = new KnowledgeContext();
        var item1 = KnowledgeItem.Create("Content 1", "source-1");
        var items2 = new[]
        {
            KnowledgeItem.Create("Content 2", "source-2"),
            KnowledgeItem.Create("Content 3", "source-3")
        };
        var item4 = KnowledgeItem.Create("Content 4", "source-4");

        // Act
        context.AddRelevantKnowledge(item1);
        context.AddRelevantKnowledge(items2);
        context.AddRelevantKnowledge(item4);

        // Assert
        Assert.Equal(4, context.RelevantKnowledge.Count);
    }

    [Fact]
    public void ShouldAddMetadataToContext_WhenUsingSetMetadata()
    {
        // Arrange
        var context = new KnowledgeContext();

        // Act
        context.SetMetadata("key1", "value1");
        context.SetMetadata("key2", 42);
        context.SetMetadata("key3", true);

        // Assert
        Assert.Equal(3, context.Metadata.Count);
        Assert.Equal("value1", context.Metadata["key1"]);
        Assert.Equal(42, context.Metadata["key2"]);
        Assert.True((bool)context.Metadata["key3"]);
    }

    [Fact]
    public void ShouldUpdateValue_WhenUsingSetMetadataWithExistingKey()
    {
        // Arrange
        var context = new KnowledgeContext();
        context.SetMetadata("key", "original");

        // Act
        context.SetMetadata("key", "updated");

        // Assert
        Assert.Single(context.Metadata);
        Assert.Equal("updated", context.Metadata["key"]);
    }

    [Fact]
    public void ShouldReturnTopKBySimilarityScore_WhenGettingTopRelevantWithScoredItems()
    {
        // Arrange
        var context = new KnowledgeContext();
        var items = new[]
        {
            new KnowledgeItem("id1", "Content 1", "source-1", null, null, 0.5),
            new KnowledgeItem("id2", "Content 2", "source-2", null, null, 0.8),
            new KnowledgeItem("id3", "Content 3", "source-3", null, null, 0.3),
            new KnowledgeItem("id4", "Content 4", "source-4", null, null, 0.9),
            new KnowledgeItem("id5", "Content 5", "source-5", null, null, 0.6)
        };
        context.AddRelevantKnowledge(items);

        // Act
        var top3 = context.GetTopRelevant(3).ToList();

        // Assert
        Assert.Equal(3, top3.Count);
        Assert.Equal(0.9, top3[0].SimilarityScore);
        Assert.Equal(0.8, top3[1].SimilarityScore);
        Assert.Equal(0.6, top3[2].SimilarityScore);
    }

    [Fact]
    public void ShouldExcludeItemsWithoutScores_WhenGettingTopRelevantWithNullScores()
    {
        // Arrange
        var context = new KnowledgeContext();
        var items = new[]
        {
            new KnowledgeItem("id1", "Content 1", "source-1", null, null, 0.5),
            new KnowledgeItem("id2", "Content 2", "source-2", null, null, null),
            new KnowledgeItem("id3", "Content 3", "source-3", null, null, 0.8),
            new KnowledgeItem("id4", "Content 4", "source-4", null, null, null),
            new KnowledgeItem("id5", "Content 5", "source-5", null, null, 0.3)
        };
        context.AddRelevantKnowledge(items);

        // Act
        var topRelevant = context.GetTopRelevant(5).ToList();

        // Assert
        Assert.Equal(3, topRelevant.Count);
        Assert.All(topRelevant, item => Assert.NotNull(item.SimilarityScore));
    }

    [Fact]
    public void ShouldReturnAllAvailable_WhenGettingTopRelevantRequestMoreThanAvailable()
    {
        // Arrange
        var context = new KnowledgeContext();
        var items = new[]
        {
            new KnowledgeItem("id1", "Content 1", "source-1", null, null, 0.5),
            new KnowledgeItem("id2", "Content 2", "source-2", null, null, 0.8)
        };
        context.AddRelevantKnowledge(items);

        // Act
        var topRelevant = context.GetTopRelevant(10).ToList();

        // Assert
        Assert.Equal(2, topRelevant.Count);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingTopRelevantWithEmptyContext()
    {
        // Arrange
        var context = new KnowledgeContext();

        // Act
        var topRelevant = context.GetTopRelevant(5);

        // Assert
        Assert.Empty(topRelevant);
    }

    [Fact]
    public void ShouldCombineWithDoubleNewline_WhenGettingCombinedContentWithDefaultSeparator()
    {
        // Arrange
        var context = new KnowledgeContext();
        var items = new[]
        {
            KnowledgeItem.Create("First content", "source-1"),
            KnowledgeItem.Create("Second content", "source-2"),
            KnowledgeItem.Create("Third content", "source-3")
        };
        context.AddRelevantKnowledge(items);

        // Act
        var combined = context.GetCombinedContent();

        // Assert
        Assert.Equal("First content\n\nSecond content\n\nThird content", combined);
    }

    [Fact]
    public void ShouldUseProvidedSeparator_WhenGettingCombinedContentWithCustomSeparator()
    {
        // Arrange
        var context = new KnowledgeContext();
        var items = new[]
        {
            KnowledgeItem.Create("First", "source-1"),
            KnowledgeItem.Create("Second", "source-2"),
            KnowledgeItem.Create("Third", "source-3")
        };
        context.AddRelevantKnowledge(items);

        // Act
        var combined = context.GetCombinedContent(" | ");

        // Assert
        Assert.Equal("First | Second | Third", combined);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenGettingCombinedContentWithEmptyContext()
    {
        // Arrange
        var context = new KnowledgeContext();

        // Act
        var combined = context.GetCombinedContent();

        // Assert
        Assert.Equal(string.Empty, combined);
    }

    [Fact]
    public void ShouldReturnItemContent_WhenGettingCombinedContentWithSingleItem()
    {
        // Arrange
        var context = new KnowledgeContext();
        context.AddRelevantKnowledge(KnowledgeItem.Create("Single content", "source"));

        // Act
        var combined = context.GetCombinedContent();

        // Assert
        Assert.Equal("Single content", combined);
    }

    [Fact]
    public void ShouldReturnReadOnlyCollection_WhenUsingRelevantKnowledge()
    {
        // Arrange
        var context = new KnowledgeContext();
        var item = KnowledgeItem.Create("Test", "source");
        context.AddRelevantKnowledge(item);

        // Act
        var knowledge = context.RelevantKnowledge;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<KnowledgeItem>>(knowledge);
        Assert.Single(knowledge);
    }

    [Fact]
    public void ShouldReturnReadOnlyDictionary_WhenUsingMetadata()
    {
        // Arrange
        var context = new KnowledgeContext();
        context.SetMetadata("key", "value");

        // Act
        var metadata = context.Metadata;

        // Assert
        Assert.IsType<Dictionary<string, object>>(metadata);
        Assert.Single(metadata);
    }

    [Fact]
    public void ShouldMaintainState_WhenUsingComplexScenarioWithMixedOperations()
    {
        // Arrange
        var context = new KnowledgeContext();

        // Add metadata
        context.SetMetadata(ParamQuery, "test query");
        context.SetMetadata("timestamp", DateTime.UtcNow);
        context.SetMetadata("user", "test-user");

        // Add knowledge items with varying similarity scores
        var items = new[]
        {
            new KnowledgeItem("id1", "Low relevance content", "doc1", null, null, 0.2),
            new KnowledgeItem("id2", "High relevance content", "doc2", null, null, 0.95),
            new KnowledgeItem("id3", "Medium relevance content", "doc3", null, null, 0.6),
            new KnowledgeItem("id4", "No score content", "doc4", null, null, null),
            new KnowledgeItem("id5", "Another high relevance", "doc5", null, null, 0.85)
        };
        context.AddRelevantKnowledge(items);

        // Act
        var top2 = context.GetTopRelevant(2).ToList();
        var combinedTop2 = string.Join(" - ", top2.Select(k => k.Content));
        var allCombined = context.GetCombinedContent(" | ");

        // Assert
        Assert.Equal(5, context.RelevantKnowledge.Count);
        Assert.Equal(3, context.Metadata.Count);
        Assert.Equal(2, top2.Count);
        Assert.Equal(0.95, top2[0].SimilarityScore);
        Assert.Equal(0.85, top2[1].SimilarityScore);
        Assert.Contains("High relevance content", combinedTop2);
        Assert.Contains("Another high relevance", combinedTop2);
        Assert.Contains("No score content", allCombined);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingKnowledgeItemWithEmbedding()
    {
        // Arrange
        var context = new KnowledgeContext();
        var embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var metadata = new Dictionary<string, object> { { "type", "document" }, { "page", 5 } };
        var item = new KnowledgeItem(
            "id1",
            "Content with embedding",
            "source",
            metadata,
            embedding,
            0.75,
            DateTime.UtcNow);

        // Act
        context.AddRelevantKnowledge(item);

        // Assert
        var retrieved = context.RelevantKnowledge[0];
        Assert.Equal(embedding, retrieved.Embedding);
        Assert.Equal(metadata, retrieved.Metadata);
        Assert.Equal(0.75, retrieved.SimilarityScore);
    }
}
