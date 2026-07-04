using Orkeon.Application.Rag;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Application.Tests.Common;

public class KnowledgeItemTests
{
    private static readonly float[] SampleEmbedding2D = [0.1f, 0.2f];
    private static readonly string[] AiResearchTags = ["AI", "ML", "Research"];
    private static readonly int[] SampleIntArray = [1, 2, 3];

    [Fact]
    public void ShouldCreateCorrectly_WhenConstructingWithAllParameters()
    {
        // Arrange
        var id = "test-id";
        var content = TestContent;
        var source = "test-source";
        var metadata = new Dictionary<string, object> { { "key", "value" } };
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var similarityScore = 0.85;
        var createdAt = DateTime.UtcNow;

        // Act
        var item = new KnowledgeItem(id, content, source, metadata, embedding, similarityScore, createdAt);

        // Assert
        Assert.Equal(id, item.Id);
        Assert.Equal(content, item.Content);
        Assert.Equal(source, item.Source);
        Assert.Equal(metadata, item.Metadata);
        Assert.Equal(embedding, item.Embedding);
        Assert.Equal(similarityScore, item.SimilarityScore);
        Assert.Equal(createdAt, item.CreatedAt);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithRequiredParametersOnly()
    {
        // Arrange
        var id = "test-id";
        var content = TestContent;
        var source = "test-source";

        // Act
        var item = new KnowledgeItem(id, content, source);

        // Assert
        Assert.Equal(id, item.Id);
        Assert.Equal(content, item.Content);
        Assert.Equal(source, item.Source);
        Assert.Null(item.Metadata);
        Assert.Null(item.Embedding);
        Assert.Null(item.SimilarityScore);
        Assert.Null(item.CreatedAt);
    }

    [Fact]
    public void ShouldAcceptNullValues_WhenConstructingWithNullOptionalParameters()
    {
        // Arrange & Act
        var item = new KnowledgeItem(
            "id",
            "content",
            "source",
            null,
            null,
            null,
            null);

        // Assert
        Assert.Null(item.Metadata);
        Assert.Null(item.Embedding);
        Assert.Null(item.SimilarityScore);
        Assert.Null(item.CreatedAt);
    }

    [Fact]
    public void ShouldGenerateIdAndSetCreatedAt_WhenCreating()
    {
        // Arrange
        var content = TestContent;
        var source = "test-source";
        var metadata = new Dictionary<string, object> { { "type", "document" } };

        // Act
        var item = KnowledgeItem.Create(content, source, metadata);

        // Assert
        Assert.NotNull(item.Id);
        Assert.NotEmpty(item.Id);
        Assert.Equal(content, item.Content);
        Assert.Equal(source, item.Source);
        Assert.Equal(metadata, item.Metadata);
        Assert.Null(item.Embedding);
        Assert.Null(item.SimilarityScore);
        Assert.NotNull(item.CreatedAt);
        Assert.True(DateTime.UtcNow - item.CreatedAt < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ShouldCreateWithNullMetadata_WhenCreatingWithoutMetadata()
    {
        // Arrange & Act
        var item = KnowledgeItem.Create("content", "source");

        // Assert
        Assert.NotNull(item.Id);
        Assert.Equal("content", item.Content);
        Assert.Equal("source", item.Source);
        Assert.Null(item.Metadata);
        Assert.NotNull(item.CreatedAt);
    }

    [Fact]
    public void ShouldMultipleCallsShouldGenerateUniqueIds_WhenCreating()
    {
        // Act
        var item1 = KnowledgeItem.Create("content", "source");
        var item2 = KnowledgeItem.Create("content", "source");
        var item3 = KnowledgeItem.Create("content", "source");

        // Assert
        Assert.NotEqual(item1.Id, item2.Id);
        Assert.NotEqual(item2.Id, item3.Id);
        Assert.NotEqual(item1.Id, item3.Id);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var id = "same-id";
        var content = "same content";
        var source = "same source";
        var metadata = new Dictionary<string, object> { { "key", "value" } };
        var embedding = new float[] { 0.1f, 0.2f };
        var score = 0.75;
        var createdAt = DateTime.UtcNow;

        // Act
        var item1 = new KnowledgeItem(id, content, source, metadata, embedding, score, createdAt);
        var item2 = new KnowledgeItem(id, content, source, metadata, embedding, score, createdAt);

        // Assert
        Assert.Equal(item1, item2);
        Assert.True(item1 == item2);
        Assert.False(item1 != item2);
        Assert.Equal(item1.GetHashCode(), item2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var baseItem = new KnowledgeItem("id", "content", "source");

        // Act & Assert - Different ID
        var differentId = new KnowledgeItem("different-id", "content", "source");
        Assert.NotEqual(baseItem, differentId);

        // Different Content
        var differentContent = new KnowledgeItem("id", "different content", "source");
        Assert.NotEqual(baseItem, differentContent);

        // Different Source
        var differentSource = new KnowledgeItem("id", "content", "different source");
        Assert.NotEqual(baseItem, differentSource);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWithSyntax()
    {
        // Arrange
        var original = KnowledgeItem.Create("original content", "original source");
        var newSimilarityScore = 0.95;
        var newEmbedding = new float[] { 0.5f, 0.6f, 0.7f };

        // Act
        var modified = original with
        {
            Content = "modified content",
            SimilarityScore = newSimilarityScore,
            Embedding = newEmbedding
        };

        // Assert
        Assert.NotEqual(original, modified);
        Assert.Equal(original.Id, modified.Id);
        Assert.Equal("modified content", modified.Content);
        Assert.Equal(original.Source, modified.Source);
        Assert.Equal(newSimilarityScore, modified.SimilarityScore);
        Assert.Equal(newEmbedding, modified.Embedding);
        Assert.Equal(original.CreatedAt, modified.CreatedAt);
    }

    [Fact]
    public void ShouldWork_WhenUsingDeconstruction()
    {
        // Arrange
        var metadata = new Dictionary<string, object> { { "test", "value" } };
        var embedding = new float[] { 0.1f };
        var item = new KnowledgeItem(
            "id-123",
            "test content",
            "test source",
            metadata,
            embedding,
            0.8,
            DateTime.UtcNow);

        // Act
        var (id, content, source, meta, embed, score, created) = item;

        // Assert
        Assert.Equal("id-123", id);
        Assert.Equal("test content", content);
        Assert.Equal("test source", source);
        Assert.Equal(metadata, meta);
        Assert.Equal(embedding, embed);
        Assert.Equal(0.8, score);
        Assert.NotNull(created);
    }

    [Fact]
    public void ShouldIncludeAllProperties_WhenCallingToString()
    {
        // Arrange
        var item = new KnowledgeItem(
            "id-123",
            "test content",
            "test source",
            new Dictionary<string, object> { { "key", "value" } },
            SampleEmbedding2D,
            0.75,
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        // Act
        var result = item.ToString();

        // Assert
        Assert.Contains("id-123", result);
        Assert.Contains("test content", result);
        Assert.Contains("test source", result);
        Assert.Contains("0.75", result);
    }

    [Fact]
    public void ShouldWithFullLifecycle_WhenUsingComplexScenario()
    {
        // Create with factory method
        var created = KnowledgeItem.Create(
            "Important knowledge about AI",
            "research-paper-2024",
            new Dictionary<string, object>
            {
                { "author", "Dr. Smith" },
                { "year", 2024 },
                { "tags", AiResearchTags }
            });

        // Simulate embedding generation
        var withEmbedding = created with
        {
            Embedding = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f]
        };

        // Simulate similarity search result
        var withScore = withEmbedding with
        {
            SimilarityScore = 0.92
        };

        // Verify the progression
        Assert.NotNull(created.Id);
        Assert.Null(created.Embedding);
        Assert.Null(created.SimilarityScore);

        Assert.Equal(created.Id, withEmbedding.Id);
        Assert.NotNull(withEmbedding.Embedding);
        Assert.Equal(5, withEmbedding.Embedding.Count);

        Assert.Equal(created.Id, withScore.Id);
        Assert.Equal(0.92, withScore.SimilarityScore);
        Assert.Equal("research-paper-2024", withScore.Source);

        // Check metadata preservation
        Assert.Equal("Dr. Smith", withScore.Metadata?["author"]);
        Assert.Equal(2024, withScore.Metadata?["year"]);
        var tags = withScore.Metadata?["tags"] as string[];
        Assert.NotNull(tags);
        Assert.Contains("AI", tags);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingMetadataWithComplexTypes()
    {
        // Arrange
        var complexMetadata = new Dictionary<string, object>
        {
            { "string", "value" },
            { "number", 42 },
            { "decimal", 3.14 },
            { "bool", true },
            { "date", DateTime.UtcNow },
            { "array", SampleIntArray },
            { "nested", new Dictionary<string, string> { { "inner", "value" } } }
        };

        // Act
        var item = KnowledgeItem.Create("content", "source", complexMetadata);

        // Assert
        Assert.Equal(7, item.Metadata?.Count);
        Assert.Equal("value", item.Metadata?["string"]);
        Assert.Equal(42, item.Metadata?["number"]);
        Assert.Equal(3.14, item.Metadata?["decimal"]);
        Assert.True((bool)item.Metadata?["bool"]!);
        Assert.IsType<DateTime>(item.Metadata?["date"]);
        Assert.IsType<int[]>(item.Metadata?["array"]);
        Assert.IsType<Dictionary<string, string>>(item.Metadata?["nested"]);
    }
}
