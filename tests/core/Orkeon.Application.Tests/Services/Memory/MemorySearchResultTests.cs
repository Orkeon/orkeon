using Orkeon.Domain.Memory;
using Orkeon.Application.Memory;
using Orkeon.Application.Services.Memory;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Application.Tests.Services.Memory;

public class MemorySearchResultTests
{
    [Fact]
    public void ShouldSetDefaultValues_WhenConstructing()
    {
        // Act
        var result = new MemorySearchResult();

        // Assert
        Assert.Equal(string.Empty, result.Id);
        Assert.Equal(string.Empty, result.Content);
        Assert.Equal(0f, result.Score);
        Assert.NotNull(result.Metadata);
        Assert.Equal(default(DateTime), result.CreatedAt);
        Assert.Null(result.AgentId);
        Assert.Null(result.Context);
        Assert.Equal(Orkeon.Domain.Memory.MemoryType.ShortTerm, result.Type);
        Assert.Null(result.Embedding);
    }

    [Fact]
    public void ShouldBeSettable_WhenAccessingProperties()
    {
        // Arrange
        var result = new MemorySearchResult();
        var metadata = MemorySearchMetadata.CreateBuilder().AddRelevance(0.8f).Build();
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var createdAt = DateTime.UtcNow;

        // Act
        result.Id = "memory-123";
        result.Content = TestContent;
        result.Score = 0.95f;
        result.Metadata = metadata;
        result.CreatedAt = createdAt;
        result.AgentId = "agent-456";
        result.Context = "test context";
        result.Type = Orkeon.Domain.Memory.MemoryType.LongTerm;
        result.Embedding = embedding;

        // Assert
        Assert.Equal("memory-123", result.Id);
        Assert.Equal(TestContent, result.Content);
        Assert.Equal(0.95f, result.Score);
        Assert.Equal(metadata, result.Metadata);
        Assert.Equal(createdAt, result.CreatedAt);
        Assert.Equal("agent-456", result.AgentId);
        Assert.Equal("test context", result.Context);
        Assert.Equal(Orkeon.Domain.Memory.MemoryType.LongTerm, result.Type);
        Assert.Equal(embedding, result.Embedding);
    }

    [Fact]
    public void ShouldCreateValidMemoryItem_WhenUsingToMemoryItemWithCompleteData()
    {
        // Arrange
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddRelevance(0.8f)
            .AddAccessCount(5)
            .Build();

        var result = new MemorySearchResult
        {
            Id = "memory-123",
            Content = "Important memory content",
            Score = 0.92f,
            Metadata = metadata,
            CreatedAt = DateTime.UtcNow,
            AgentId = "agent-456",
            Context = "task execution",
            Type = Orkeon.Domain.Memory.MemoryType.LongTerm,
            Embedding = embedding
        };

        // Act
        var memoryItem = result.ToMemoryItem();

        // Assert
        Assert.NotNull(memoryItem);
        Assert.Equal("Important memory content", memoryItem.Content);
        Assert.Equal(embedding, memoryItem.Embedding);
        Assert.Equal(0.5f, memoryItem.Importance);
        Assert.Equal("search_result", memoryItem.Metadata.Source);
        Assert.NotNull(memoryItem.Metadata.CustomProperties);
        Assert.Contains("type", memoryItem.Metadata.CustomProperties.Keys);
        Assert.Equal("LongTerm", memoryItem.Metadata.CustomProperties["type"]);
        Assert.Contains("agent_id", memoryItem.Metadata.CustomProperties.Keys);
        Assert.Equal("agent-456", memoryItem.Metadata.CustomProperties["agent_id"]);
        Assert.Contains("context", memoryItem.Metadata.CustomProperties.Keys);
        Assert.Equal("task execution", memoryItem.Metadata.CustomProperties["context"]);
    }

    [Fact]
    public void ShouldCreateValidMemoryItem_WhenUsingToMemoryItemWithMinimalData()
    {
        // Arrange
        var result = new MemorySearchResult
        {
            Content = "Minimal content",
            Score = 0.5f
        };

        // Act
        var memoryItem = result.ToMemoryItem();

        // Assert
        Assert.NotNull(memoryItem);
        Assert.Equal("Minimal content", memoryItem.Content);
        Assert.Null(memoryItem.Embedding);
        Assert.Equal(0.5f, memoryItem.Importance);
        Assert.Equal("search_result", memoryItem.Metadata.Source);
        Assert.NotNull(memoryItem.Metadata.CustomProperties);
        Assert.Contains("type", memoryItem.Metadata.CustomProperties.Keys);
        Assert.Equal("ShortTerm", memoryItem.Metadata.CustomProperties["type"]);
    }

    [Fact]
    public void ShouldNotIncludeThemInMetadata_WhenUsingToMemoryItemWithNullAgentIdAndContext()
    {
        // Arrange
        var result = new MemorySearchResult
        {
            Content = "Content without agent or context",
            AgentId = null,
            Context = null
        };

        // Act
        var memoryItem = result.ToMemoryItem();

        // Assert
        Assert.NotNull(memoryItem);
        Assert.NotNull(memoryItem.Metadata.CustomProperties);
        Assert.DoesNotContain("agent_id", memoryItem.Metadata.CustomProperties.Keys);
        Assert.DoesNotContain("context", memoryItem.Metadata.CustomProperties.Keys);
        Assert.Contains("type", memoryItem.Metadata.CustomProperties.Keys);
    }

    [Fact]
    public void ShouldNotIncludeThemInMetadata_WhenUsingToMemoryItemWithEmptyAgentIdAndContext()
    {
        // Arrange
        var result = new MemorySearchResult
        {
            Content = "Content with empty agent and context",
            AgentId = string.Empty,
            Context = string.Empty
        };

        // Act
        var memoryItem = result.ToMemoryItem();

        // Assert
        Assert.NotNull(memoryItem);
        Assert.NotNull(memoryItem.Metadata.CustomProperties);
        Assert.DoesNotContain("agent_id", memoryItem.Metadata.CustomProperties.Keys);
        Assert.DoesNotContain("context", memoryItem.Metadata.CustomProperties.Keys);
    }

    [Fact]
    public void ShouldCreateValidSearchResult_WhenUsingFromMemoryItemWithCompleteMemoryItem()
    {
        // Arrange
        var embedding = new float[] { 0.4f, 0.5f, 0.6f };
        var agentId = Orkeon.Domain.Common.AgentId.Create();
        var customProperties = new Dictionary<string, string>
        {
            ["type"] = "LongTerm",
            ["context"] = "test context",
            ["custom_field"] = "custom_value"
        };

        var memoryItem = MemoryItem.Create(
            content: "Memory item content",
            embedding: embedding,
            importance: 0.8f,
            source: "test_source",
            tags: null,
            createdBy: agentId,
            customProperties: customProperties
        );

        var score = 0.85f;

        // Act
        var searchResult = MemorySearchResult.FromMemoryItem(memoryItem, score);

        // Assert
        Assert.NotNull(searchResult);
        Assert.Equal(memoryItem.Id, searchResult.Id);
        Assert.Equal("Memory item content", searchResult.Content);
        Assert.Equal(0.85f, searchResult.Score);
        Assert.Equal(memoryItem.Timestamp, searchResult.CreatedAt);
        Assert.Equal(agentId.ToString(), searchResult.AgentId);
        Assert.Equal("test context", searchResult.Context);
        Assert.Equal(Orkeon.Domain.Memory.MemoryType.LongTerm, searchResult.Type);
        Assert.Equal(embedding, searchResult.Embedding);
        Assert.NotNull(searchResult.Metadata);
    }

    [Fact]
    public void ShouldCreateValidSearchResult_WhenUsingFromMemoryItemWithMinimalMemoryItem()
    {
        // Arrange
        var memoryItem = MemoryItem.Create(
            content: "Minimal memory item",
            embedding: null,
            importance: 0.3f,
            source: "minimal_source",
            tags: null,
            createdBy: null,
            customProperties: null
        );

        var score = 0.45f;

        // Act
        var searchResult = MemorySearchResult.FromMemoryItem(memoryItem, score);

        // Assert
        Assert.NotNull(searchResult);
        Assert.Equal(memoryItem.Id, searchResult.Id);
        Assert.Equal("Minimal memory item", searchResult.Content);
        Assert.Equal(0.45f, searchResult.Score);
        Assert.Equal(memoryItem.Timestamp, searchResult.CreatedAt);
        Assert.Null(searchResult.AgentId);
        Assert.Null(searchResult.Context);
        Assert.Equal(Orkeon.Domain.Memory.MemoryType.ShortTerm, searchResult.Type); // Default
        Assert.Null(searchResult.Embedding);
    }

    [Fact]
    public void ShouldUseDefault_WhenUsingFromMemoryItemWithInvalidMemoryTypeInCustomProperties()
    {
        // Arrange
        var customProperties = new Dictionary<string, string>
        {
            ["type"] = "InvalidMemoryType"
        };

        var memoryItem = MemoryItem.Create(
            content: "Content with invalid type",
            embedding: null,
            importance: 0.5f,
            source: "test_source",
            tags: null,
            createdBy: null,
            customProperties: customProperties
        );

        // Act
        var searchResult = MemorySearchResult.FromMemoryItem(memoryItem, 0.5f);

        // Assert
        Assert.Equal(Orkeon.Domain.Memory.MemoryType.ShortTerm, searchResult.Type); // Should use default
    }

    [Fact]
    public void ShouldParseCorrectly_WhenUsingFromMemoryItemWithValidMemoryTypes()
    {
        // Arrange
        var memoryTypes = new[]
        {
            (Orkeon.Domain.Memory.MemoryType.ShortTerm, "ShortTerm"),
            (Orkeon.Domain.Memory.MemoryType.LongTerm, "LongTerm"),
            (Orkeon.Domain.Memory.MemoryType.Episodic, "Episodic")
        };

        foreach (var (expectedType, typeString) in memoryTypes)
        {
            var customProperties = new Dictionary<string, string>
            {
                ["type"] = typeString
            };

            var memoryItem = MemoryItem.Create(
                content: $"Content for {typeString}",
                embedding: null,
                importance: 0.5f,
                source: "test_source",
                tags: null,
                createdBy: null,
                customProperties: customProperties
            );

            // Act
            var searchResult = MemorySearchResult.FromMemoryItem(memoryItem, 0.5f);

            // Assert
            Assert.Equal(expectedType, searchResult.Type);
        }
    }

    [Fact]
    public void ShouldSetContextToNull_WhenUsingFromMemoryItemWithNullContext()
    {
        // Arrange
        var customProperties = new Dictionary<string, string>
        {
            ["other_property"] = "value"
        };

        var memoryItem = MemoryItem.Create(
            content: "Content without context",
            embedding: null,
            importance: 0.5f,
            source: "test_source",
            tags: null,
            createdBy: null,
            customProperties: customProperties
        );

        // Act
        var searchResult = MemorySearchResult.FromMemoryItem(memoryItem, 0.5f);

        // Assert
        Assert.Null(searchResult.Context);
    }

    [Fact]
    public void ShouldBeValid_WhenScoringWithBoundaryValues()
    {
        // Arrange
        var result = new MemorySearchResult();

        // Act & Assert
        result.Score = 0.0f;
        Assert.Equal(0.0f, result.Score);

        result.Score = 1.0f;
        Assert.Equal(1.0f, result.Score);

        result.Score = -0.5f;
        Assert.Equal(-0.5f, result.Score);

        result.Score = 1.5f;
        Assert.Equal(1.5f, result.Score);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingEmbeddingWithDifferentSizes()
    {
        // Arrange
        var result = new MemorySearchResult();

        // Act & Assert
        result.Embedding = [];
        Assert.Empty(result.Embedding);

        result.Embedding = [0.1f];
        Assert.Single(result.Embedding);

        result.Embedding = new float[384]; // Common embedding size
        Assert.Equal(384, result.Embedding.Count);

        result.Embedding = new float[768]; // Another common size
        Assert.Equal(768, result.Embedding.Count);
    }
}
