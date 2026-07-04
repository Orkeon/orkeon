using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Memory;

public class MemoryItemTests
{
    private static readonly string[] CriticalCustomerTags = ["critical", "customer_data"];
    private static readonly string[] Tag1Tag2 = ["tag1", "tag2"];
    private static readonly string[] ExistingTag = ["existing"];
    private static readonly string[] FourTags = ["tag1", "tag2", "tag3", "tag4"];
    private static readonly string[] InitialTags = ["initial1", "initial2"];
    private static readonly string[] CustomerVipSupportTags = ["customer", "vip", "support"];
    private static readonly string[] ConfigDbCriticalTags = ["config", "database", "critical"];
    #region Test Helpers

    private static float[] CreateTestEmbedding(int dimensions = 128)
    {
        return Enumerable.Range(0, dimensions).Select(i => (float)i / dimensions).ToArray();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldCreateMemoryItem_WhenConstructingWithValidContent()
    {
        // Arrange
        var content = "This is a test memory";

        // Act
        var memory = MemoryItem.Create(content);

        // Assert
        Assert.NotNull(memory.Id);
        Assert.NotNull(memory.Id); // ID is ULID-based EntityId
        Assert.NotNull(memory.Id); // ID is ULID-based EntityId
        Assert.Equal(content, memory.Content);
        Assert.Null(memory.Embedding);
        Assert.Equal(0.5f, memory.Importance);
        Assert.NotNull(memory.Metadata);
        Assert.Equal("unknown", memory.Source);
        Assert.Empty(memory.Tags);
        Assert.Equal(0, memory.AccessCount);
        Assert.Equal(memory.Timestamp, memory.LastAccessed);
    }

    [Fact]
    public void ShouldSetAllProperties_WhenConstructingWithAllParameters()
    {
        // Arrange
        var content = "Important memory";
        var embedding = CreateTestEmbedding();
        var importance = 0.9f;
        var source = "data_processing";
        var tags = CriticalCustomerTags;
        var createdBy = AgentId.From(Guid.NewGuid());
        var customProperties = new Dictionary<string, string>
        {
            ["category"] = "financial",
            ["priority"] = "high"
        };

        // Act
        var memory = MemoryItem.Create(content, embedding, importance, source, tags, createdBy, customProperties);

        // Assert
        Assert.Equal(content, memory.Content);
        Assert.Same(embedding, memory.Embedding);
        Assert.Equal(importance, memory.Importance);
        Assert.Equal(source, memory.Source);
        Assert.Equal(tags, memory.Tags);
        Assert.Equal(createdBy, memory.Metadata.CreatedBy);
        Assert.NotNull(memory.Metadata.CustomProperties);
        Assert.Equal("financial", memory.Metadata.CustomProperties["category"]);
        Assert.Equal("high", memory.Metadata.CustomProperties["priority"]);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithEmptyContent()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            MemoryItem.Create(""));
        Assert.Equal("content", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithWhitespaceContent()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            MemoryItem.Create("   "));
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithImportanceBelowZero()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MemoryItem.Create("Content", importance: -0.1f));
        Assert.Contains("Importance must be between 0 and 1", exception.Message);
        Assert.Equal("importance", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithImportanceAboveOne()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MemoryItem.Create("Content", importance: 1.1f));
        Assert.Contains("Importance must be between 0 and 1", exception.Message);
    }

    [Fact]
    public void ShouldUseDefault_WhenConstructingWithNullSource()
    {
        // Act
        var memory = MemoryItem.Create("Content", source: null);

        // Assert
        Assert.Equal("unknown", memory.Source);
    }

    [Fact]
    public void ShouldReturnEmptyArray_WhenConstructingWithNullTags()
    {
        // Act
        var memory = MemoryItem.Create("Content", tags: null);

        // Assert
        Assert.NotNull(memory.Tags);
        Assert.Empty(memory.Tags);
    }

    [Fact]
    public void ShouldSetTimestamps_WhenConstructing()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var memory = MemoryItem.Create("Content");
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(memory.Timestamp >= before);
        Assert.True(memory.Timestamp <= after);
        Assert.Equal(memory.Timestamp, memory.Metadata.CreatedAt);
        Assert.Null(memory.Metadata.LastAccessedAt);
        Assert.Equal(memory.Timestamp, memory.LastAccessed);
    }

    [Fact]
    public void ShouldInitializeMetadata_WhenConstructing()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var tags = Tag1Tag2;
        var importance = 0.8f;

        // Act
        var memory = MemoryItem.Create("Content", importance: importance, tags: tags, createdBy: agentId);

        // Assert
        Assert.NotNull(memory.Metadata);
        Assert.Equal(memory.Timestamp, memory.Metadata.CreatedAt);
        Assert.Null(memory.Metadata.LastAccessedAt);
        Assert.Equal("unknown", memory.Metadata.Source);
        Assert.Equal(importance, memory.Metadata.Relevance);
        Assert.Equal(tags, memory.Metadata.Tags);
        Assert.Equal(agentId, memory.Metadata.CreatedBy);
        Assert.Equal(0, memory.Metadata.AccessCount);
    }

    #endregion

    #region IncrementAccessCount Tests

    [Fact]
    public void ShouldIncreaseCount_WhenUsingIncrementAccessCount()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");
        var initialCount = memory.AccessCount;

        // Act
        memory.IncrementAccessCount();

        // Assert
        Assert.Equal(initialCount + 1, memory.AccessCount);
        Assert.Equal(initialCount + 1, memory.Metadata.AccessCount);
    }

    [Fact]
    public void ShouldUpdateLastAccessedTime_WhenUsingIncrementAccessCount()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");
        var initialLastAccessed = memory.LastAccessed;
        ClockAdvance.UntilStrictlyAfter(initialLastAccessed); // Ensure time difference (R5.6)

        // Act
        memory.IncrementAccessCount();

        // Assert
        Assert.True(memory.LastAccessed > initialLastAccessed);
        Assert.NotNull(memory.Metadata.LastAccessedAt);
        Assert.Equal(memory.Metadata.LastAccessedAt.Value, memory.LastAccessed);
    }

    [Fact]
    public void ShouldAccumulate_WhenUsingIncrementAccessCountWithMultipleTimes()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");
        var timestamps = new List<DateTime>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            memory.IncrementAccessCount();
            timestamps.Add(memory.LastAccessed);
            ClockAdvance.UntilStrictlyAfter(memory.LastAccessed);
        }

        // Assert
        Assert.Equal(5, memory.AccessCount);

        // Verify timestamps are increasing
        for (int i = 1; i < timestamps.Count; i++)
        {
            Assert.True(timestamps[i] > timestamps[i - 1]);
        }
    }

    #endregion

    #region UpdateImportance Tests

    [Fact]
    public void ShouldUpdate_WhenUpdatingImportanceWithValidValue()
    {
        // Arrange
        var memory = MemoryItem.Create("Content", importance: 0.5f);

        // Act
        memory.UpdateImportance(0.9f);

        // Assert
        Assert.Equal(0.9f, memory.Importance);
    }

    [Fact]
    public void ShouldUpdate_WhenUpdatingImportanceWithZero()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act
        memory.UpdateImportance(0f);

        // Assert
        Assert.Equal(0f, memory.Importance);
    }

    [Fact]
    public void ShouldUpdate_WhenUpdatingImportanceWithOne()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act
        memory.UpdateImportance(1f);

        // Assert
        Assert.Equal(1f, memory.Importance);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingImportanceWithNegativeValue()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            memory.UpdateImportance(-0.01f));
        Assert.Contains("Importance must be between 0 and 1", exception.Message);
        Assert.Equal("newImportance", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingImportanceWithValueGreaterThanOne()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            memory.UpdateImportance(1.01f));
        Assert.Contains("Importance must be between 0 and 1", exception.Message);
    }

    #endregion

    #region AddTag Tests

    [Fact]
    public void ShouldAdd_WhenAddingTagWithValidTag()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act
        memory.AddTag("important");

        // Assert
        Assert.Single(memory.Tags);
        Assert.Contains("important", memory.Tags);
    }

    [Fact]
    public void ShouldThrow_WhenAddingTagWithEmptyTag()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            memory.AddTag(""));
        Assert.Equal("tag", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenAddingTagWithWhitespaceTag()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            memory.AddTag("   "));
    }

    [Fact]
    public void ShouldNotAddTwice_WhenAddingTagWithDuplicateTag()
    {
        // Arrange
        var memory = MemoryItem.Create("Content", tags: ExistingTag);

        // Act
        memory.AddTag("existing");

        // Assert
        Assert.Single(memory.Tags);
        Assert.Equal("existing", memory.Tags[0]);
    }

    [Fact]
    public void ShouldMaintainAll_WhenAddingTagWithMultipleTags()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");
        var tags = FourTags;

        // Act
        foreach (var tag in tags)
        {
            memory.AddTag(tag);
        }

        // Assert
        Assert.Equal(4, memory.Tags.Count);
        Assert.All(tags, tag => Assert.Contains(tag, memory.Tags));
    }

    [Fact]
    public void ShouldAppend_WhenAddingTagToExistingTags()
    {
        // Arrange
        var initialTags = InitialTags;
        var memory = MemoryItem.Create("Content", tags: initialTags);

        // Act
        memory.AddTag("new1");
        memory.AddTag("new2");

        // Assert
        Assert.Equal(4, memory.Tags.Count);
        Assert.Contains("initial1", memory.Tags);
        Assert.Contains("initial2", memory.Tags);
        Assert.Contains("new1", memory.Tags);
        Assert.Contains("new2", memory.Tags);
    }

    #endregion

    #region AddCustomProperty Tests

    [Fact]
    public void ShouldAdd_WhenAddingCustomPropertyWithValidKeyValue()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act
        memory.AddCustomProperty("category", "financial");

        // Assert
        Assert.NotNull(memory.Metadata.CustomProperties);
        Assert.Single(memory.Metadata.CustomProperties);
        Assert.Equal("financial", memory.Metadata.CustomProperties["category"]);
    }

    [Fact]
    public void ShouldThrow_WhenAddingCustomPropertyWithEmptyKey()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            memory.AddCustomProperty("", "value"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenAddingCustomPropertyWithWhitespaceKey()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            memory.AddCustomProperty("   ", "value"));
    }

    [Fact]
    public void ShouldSetEmptyString_WhenAddingCustomPropertyWithNullValue()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");

        // Act
        memory.AddCustomProperty("key", null!);

        // Assert
        Assert.Equal(string.Empty, memory.Metadata.CustomProperties!["key"]);
    }

    [Fact]
    public void ShouldUpdate_WhenAddingCustomPropertyWithExistingKey()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");
        memory.AddCustomProperty("status", "pending");

        // Act
        memory.AddCustomProperty("status", "completed");

        // Assert
        Assert.Single(memory.Metadata.CustomProperties!);
        Assert.Equal("completed", memory.Metadata.CustomProperties!["status"]);
    }

    [Fact]
    public void ShouldMaintainAll_WhenAddingCustomPropertyWithMultipleProperties()
    {
        // Arrange
        var memory = MemoryItem.Create("Content");
        var properties = new Dictionary<string, string>
        {
            ["prop1"] = "value1",
            ["prop2"] = "value2",
            ["prop3"] = "value3"
        };

        // Act
        foreach (var kvp in properties)
        {
            memory.AddCustomProperty(kvp.Key, kvp.Value);
        }

        // Assert
        Assert.Equal(3, memory.Metadata.CustomProperties!.Count);
        foreach (var kvp in properties)
        {
            Assert.Equal(kvp.Value, memory.Metadata.CustomProperties[kvp.Key]);
        }
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldMemoryLifecycle_WhenUsingComplexScenario()
    {
        // Arrange - Create a memory with full context
        var agentId = AgentId.From(Guid.NewGuid());
        var embedding = CreateTestEmbedding(384); // Larger embedding
        var initialTags = CustomerVipSupportTags;
        var customProps = new Dictionary<string, string>
        {
            ["ticket_id"] = "SUP-12345",
            ["customer_tier"] = "platinum"
        };

        var memory = MemoryItem.Create(
            "Customer reported critical issue with payment processing",
            embedding,
            0.95f, // High importance
            "support_system",
            initialTags,
            agentId,
            customProps);

        // Act - Simulate memory usage over time
        // First access
        ClockAdvance.Tick();
        memory.IncrementAccessCount();

        // Add more context
        memory.AddTag("urgent");
        memory.AddCustomProperty("resolution_time", "2h");

        // Second access
        ClockAdvance.Tick();
        memory.IncrementAccessCount();

        // Update importance after resolution
        memory.UpdateImportance(0.7f);
        memory.AddTag("resolved");
        memory.AddCustomProperty("resolution", "Fixed API timeout issue");

        // Third access for reference
        ClockAdvance.Tick();
        memory.IncrementAccessCount();

        // Assert - Verify complete state
        Assert.Equal("Customer reported critical issue with payment processing", memory.Content);
        Assert.Equal(embedding, memory.Embedding);
        Assert.Equal(0.7f, memory.Importance);
        Assert.Equal("support_system", memory.Source);
        Assert.Equal(3, memory.AccessCount);

        // Verify tags
        Assert.Equal(5, memory.Tags.Count);
        Assert.Contains("customer", memory.Tags);
        Assert.Contains("urgent", memory.Tags);
        Assert.Contains("resolved", memory.Tags);

        // Verify custom properties
        Assert.Equal(4, memory.Metadata.CustomProperties!.Count);
        Assert.Equal("SUP-12345", memory.Metadata.CustomProperties["ticket_id"]);
        Assert.Equal("2h", memory.Metadata.CustomProperties["resolution_time"]);
        Assert.Equal("Fixed API timeout issue", memory.Metadata.CustomProperties["resolution"]);

        // Verify timestamps
        Assert.True(memory.LastAccessed > memory.Timestamp);
        Assert.Equal(agentId, memory.Metadata.CreatedBy);
    }

    [Fact]
    public void ShouldMemoryWithEmbeddings_WhenUsingComplexScenario()
    {
        // Arrange - Create memories with different embeddings for similarity
        var baseEmbedding = Enumerable.Range(0, 128).Select(i => (float)Math.Sin(i * 0.1)).ToArray();
        var similarEmbedding = Enumerable.Range(0, 128).Select(i => (float)Math.Sin(i * 0.1) + 0.01f).ToArray();
        var differentEmbedding = Enumerable.Range(0, 128).Select(i => (float)Math.Cos(i * 0.2)).ToArray();

        var memory1 = MemoryItem.Create("Python code for data analysis", baseEmbedding, 0.8f);
        var memory2 = MemoryItem.Create("Python script for analytics", similarEmbedding, 0.85f);
        var memory3 = MemoryItem.Create("Java application deployment", differentEmbedding, 0.7f);

        // Act - Simulate embedding-based operations
        // Calculate simple cosine similarity (for demonstration)
        float CosineSimilarity(float[] a, float[] b)
        {
            float dotProduct = 0f, magnitudeA = 0f, magnitudeB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                magnitudeA += a[i] * a[i];
                magnitudeB += b[i] * b[i];
            }
            return dotProduct / (float)(Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
        }

        var sim1_2 = CosineSimilarity(memory1.Embedding!.ToArray(), memory2.Embedding!.ToArray());
        var sim1_3 = CosineSimilarity(memory1.Embedding!.ToArray(), memory3.Embedding!.ToArray());
        var sim2_3 = CosineSimilarity(memory2.Embedding!.ToArray(), memory3.Embedding!.ToArray());

        // Assert - Verify embedding-based relationships
        Assert.NotNull(memory1.Embedding);
        Assert.NotNull(memory2.Embedding);
        Assert.NotNull(memory3.Embedding);
        Assert.Equal(128, memory1.Embedding.Count);

        // Similar memories should have higher similarity
        Assert.True(sim1_2 > sim1_3);
        Assert.True(sim1_2 > sim2_3);

        // Tag based on content similarity
        if (sim1_2 > 0.9f)
        {
            memory1.AddTag("python");
            memory2.AddTag("python");
        }

        Assert.Contains("python", memory1.Tags);
        Assert.Contains("python", memory2.Tags);
    }

    [Fact]
    public void ShouldHighFrequencyAccessPattern_WhenUsingComplexScenario()
    {
        // Arrange - Memory representing frequently accessed configuration
        var memory = MemoryItem.Create(
            "Database connection string: Server=prod-db;Database=app;",
            importance: 1.0f, // Critical importance
            source: "configuration",
            tags: ConfigDbCriticalTags);

        memory.AddCustomProperty("environment", "production");
        memory.AddCustomProperty("last_validated", DateTime.UtcNow.ToString("O"));

        // Act - Simulate high-frequency access pattern
        var accessTimes = new List<DateTime>();
        for (int i = 0; i < 100; i++)
        {
            memory.IncrementAccessCount();
            if (i % 10 == 0)
            {
                accessTimes.Add(memory.LastAccessed);
                ClockAdvance.Tick(); // Small deterministic gap every 10 accesses (R5.6)
            }
        }

        // Update metadata periodically
        if (memory.AccessCount > 50)
        {
            memory.AddTag("high_frequency");
            memory.AddCustomProperty("cache_candidate", "true");
        }

        // Assert
        Assert.Equal(100, memory.AccessCount);
        Assert.Equal(1.0f, memory.Importance); // Should remain critical
        Assert.Contains("high_frequency", memory.Tags);
        Assert.Equal("true", memory.Metadata.CustomProperties!["cache_candidate"]);

        // Verify access time progression
        for (int i = 1; i < accessTimes.Count; i++)
        {
            Assert.True(accessTimes[i] >= accessTimes[i - 1]);
        }
    }

    [Fact]
    public void ShouldEvolvingMemoryImportance_WhenUsingComplexScenario()
    {
        // Arrange - Memory that changes importance over time
        var memory = MemoryItem.Create(
            "Experimental feature flag: enable_new_algorithm",
            importance: 0.3f, // Low initial importance
            source: "feature_flags");

        // Act - Simulate importance evolution
        // Phase 1: Experimentation
        memory.AddTag("experimental");
        memory.AddCustomProperty("rollout_percentage", "1");

        // Phase 2: Positive results
        memory.UpdateImportance(0.6f);
        memory.AddTag("promising");
        memory.AddCustomProperty("rollout_percentage", "10");
        memory.AddCustomProperty("success_rate", "98.5");

        // Phase 3: Full rollout
        memory.UpdateImportance(0.95f);
        memory.AddTag("production");
        memory.AddTag("successful");
        memory.AddCustomProperty("rollout_percentage", "100");
        memory.AddCustomProperty("impact", "15% performance improvement");

        // Assert
        Assert.Equal(0.95f, memory.Importance);
        Assert.Equal(4, memory.Tags.Count);
        Assert.Contains("experimental", memory.Tags);
        Assert.Contains("production", memory.Tags);
        Assert.Equal("100", memory.Metadata.CustomProperties!["rollout_percentage"]);
        Assert.Equal("15% performance improvement", memory.Metadata.CustomProperties["impact"]);
    }

    #endregion

    #region ShouldPromoteToLongTerm Tests

    [Theory]
    [InlineData(0.71f, true)]
    [InlineData(0.8f, true)]
    [InlineData(1.0f, true)]
    [InlineData(0.7f, false)]
    [InlineData(0.5f, false)]
    [InlineData(0.0f, false)]
    public void ShouldPromoteToLongTerm_WhenImportanceExceedsThreshold(float importance, bool expected)
    {
        // Arrange
        var memory = MemoryItem.Create(TestContent, importance: importance);

        // Act
        var result = memory.ShouldPromoteToLongTerm();

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion
}
