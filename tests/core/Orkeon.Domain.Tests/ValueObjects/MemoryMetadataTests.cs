using Orkeon.Domain.Common;
using Orkeon.Domain.Memory.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class MemoryMetadataTests
{
    private static readonly string[] ImportantTechnicalPythonTags = ["important", "technical", "python"];
    private static readonly string[] Tag1Tag2 = ["tag1", "tag2"];
    private static readonly string[] SingleTestTag = ["test"];
    private static readonly string[] SingleTagTag = ["tag"];
    private static readonly string[] QuestionTechnicalUrgentTags = ["question", "technical", "urgent"];
    private static readonly string[] CoreReferenceApiTags = ["core", "reference", "api"];
    private static readonly string[] RecentImportantTags = ["recent", "important"];
    private static readonly string[] ReferenceTags = ["reference"];
    private static readonly string[] RecentTags = ["recent"];
    private static readonly string[] TechnicalReferenceTags = ["technical", "reference"];
    private static readonly string[] NewUnprocessedTags = ["new", "unprocessed"];
    private static readonly string[] ProcessedAnsweredCachedTags = ["processed", "answered", "cached"];
    private static readonly string[] ImportantTags = ["important"];
    private static readonly string[] FrequentTags = ["frequent"];
    private static readonly string[] OldTags = ["old"];
    private static readonly string[] NewTags = ["new"];

    #region Constructor Tests

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithRequiredParameters()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var lastAccessedAt = createdAt.AddMinutes(5);
        var source = "conversation";
        var relevance = 0.85;

        // Act
        var metadata = MemoryMetadata.Create(
            createdAt,
            lastAccessedAt,
            source,
            relevance);

        // Assert
        Assert.Equal(createdAt, metadata.CreatedAt);
        Assert.Equal(lastAccessedAt, metadata.LastAccessedAt);
        Assert.Equal(source, metadata.Source);
        Assert.Equal(relevance, metadata.Relevance);
        Assert.Null(metadata.Tags);
        Assert.Null(metadata.CreatedBy);
        Assert.Equal(0, metadata.AccessCount);
        Assert.Null(metadata.CustomProperties);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithAllParameters()
    {
        // Arrange
        var createdAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var lastAccessedAt = createdAt.AddHours(2);
        var source = "user_input";
        var relevance = 0.95;
        var tags = ImportantTechnicalPythonTags;
        var createdBy = AgentId.From(Guid.NewGuid());
        var accessCount = 5;
        var customProperties = new Dictionary<string, string>
        {
            { "category", "programming" },
            { "language", "python" }
        };

        // Act
        var metadata = MemoryMetadata.Create(
            createdAt,
            lastAccessedAt,
            source,
            relevance,
            tags,
            createdBy,
            accessCount,
            customProperties);

        // Assert
        Assert.Equal(createdAt, metadata.CreatedAt);
        Assert.Equal(lastAccessedAt, metadata.LastAccessedAt);
        Assert.Equal(source, metadata.Source);
        Assert.Equal(relevance, metadata.Relevance);
        Assert.Equal(tags, metadata.Tags);
        Assert.Equal(createdBy, metadata.CreatedBy);
        Assert.Equal(accessCount, metadata.AccessCount);
        Assert.Equal(customProperties, metadata.CustomProperties);
    }

    [Fact]
    public void ShouldBeAllowed_WhenConstructingWithNullLastAccessedAt()
    {
        // Act
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "document",
            0.7);

        // Assert
        Assert.Null(metadata.LastAccessedAt);
    }

    #endregion

    #region Record Behavior Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var agentId = AgentId.From(Guid.NewGuid());
        var tags = Tag1Tag2;
        var properties = new Dictionary<string, string> { { "key", "value" } };

        var metadata1 = MemoryMetadata.Create(
            createdAt,
            createdAt.AddMinutes(10),
            "api",
            0.8,
            tags,
            agentId,
            3,
            properties);

        var metadata2 = MemoryMetadata.Create(
            createdAt,
            createdAt.AddMinutes(10),
            "api",
            0.8,
            tags,
            agentId,
            3,
            properties);

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1 == metadata2);
        Assert.False(metadata1 != metadata2);
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var metadata1 = MemoryMetadata.Create(createdAt, null, "source1", 0.8);
        var metadata2 = MemoryMetadata.Create(createdAt, null, "source2", 0.8);

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
        Assert.False(metadata1 == metadata2);
        Assert.True(metadata1 != metadata2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "original",
            0.5,
            accessCount: 2);

        // Act
        var modified = original with { Source = "modified", Relevance = 0.9 };

        // Assert
        Assert.Equal("original", original.Source);
        Assert.Equal("modified", modified.Source);
        Assert.Equal(0.5, original.Relevance);
        Assert.Equal(0.9, modified.Relevance);
        Assert.Equal(original.CreatedAt, modified.CreatedAt);
        Assert.Equal(original.AccessCount, modified.AccessCount);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30),
            "test_source",
            0.75,
            SingleTestTag,
            AgentId.From(Guid.NewGuid()),
            10);

        // Act
        var result = metadata.ToJson();

        // Assert
        Assert.Contains("test_source", result);
        Assert.Contains("0.75", result);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingDeconstruct()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var lastAccessedAt = createdAt.AddMinutes(15);
        var source = "deconstructed";
        var relevance = 0.66;
        var tags = SingleTagTag;
        var createdBy = AgentId.From(Guid.NewGuid());
        var accessCount = 7;
        var customProperties = new Dictionary<string, string> { { "prop", "value" } };

        var metadata = MemoryMetadata.Create(
            createdAt,
            lastAccessedAt,
            source,
            relevance,
            tags,
            createdBy,
            accessCount,
            customProperties);

        // Act & Assert
        Assert.Equal(createdAt, metadata.Access.CreatedAt);
        Assert.Equal(lastAccessedAt, metadata.Access.LastAccessedAt);
        Assert.Equal(accessCount, metadata.Access.AccessCount);
        Assert.Equal(source, metadata.Source);
        Assert.Equal(relevance, metadata.Relevance);
        Assert.Equal(tags, metadata.Tags);
        Assert.Equal(createdBy, metadata.CreatedBy);
        Assert.Equal(customProperties, metadata.CustomProperties);
    }

    #endregion

    #region IncrementAccess Tests

    [Fact]
    public void ShouldIncrementCountAndUpdateTime_WhenIncrementingAccess()
    {
        // Arrange
        var original = MemoryMetadata.Create(
            DateTime.UtcNow.AddHours(-1),
            null,
            "memory",
            0.7,
            accessCount: 0);

        var beforeIncrement = DateTime.UtcNow;

        // Act
        var updated = original.IncrementAccess();

        var afterIncrement = DateTime.UtcNow;

        // Assert
        Assert.Equal(0, original.AccessCount);
        Assert.Null(original.LastAccessedAt);

        Assert.Equal(1, updated.AccessCount);
        Assert.NotNull(updated.LastAccessedAt);
        Assert.True(updated.LastAccessedAt >= beforeIncrement);
        Assert.True(updated.LastAccessedAt <= afterIncrement);

        // Other properties should remain unchanged
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal(original.Source, updated.Source);
        Assert.Equal(original.Relevance, updated.Relevance);
    }

    [Fact]
    public void ShouldIncrementCorrectly_WhenIncrementingAccessWithMultipleTimes()
    {
        // Arrange
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow.AddDays(-1),
            null,
            "memory",
            0.8);

        // Act
        var accessed1 = metadata.IncrementAccess();
        var accessed2 = accessed1.IncrementAccess();
        var accessed3 = accessed2.IncrementAccess();

        // Assert
        Assert.Equal(0, metadata.AccessCount);
        Assert.Equal(1, accessed1.AccessCount);
        Assert.Equal(2, accessed2.AccessCount);
        Assert.Equal(3, accessed3.AccessCount);

        // Each should have different LastAccessedAt times
        Assert.NotNull(accessed1.LastAccessedAt);
        Assert.NotNull(accessed2.LastAccessedAt);
        Assert.NotNull(accessed3.LastAccessedAt);

        // Later accesses should have later times (or equal due to time resolution)
        Assert.True(accessed2.LastAccessedAt >= accessed1.LastAccessedAt);
        Assert.True(accessed3.LastAccessedAt >= accessed2.LastAccessedAt);
    }

    [Fact]
    public void ShouldUpdateTime_WhenIncrementingAccessWithExistingLastAccessedAt()
    {
        // Arrange
        var oldAccessTime = DateTime.UtcNow.AddHours(-5);
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow.AddDays(-1),
            oldAccessTime,
            "memory",
            0.9,
            accessCount: 10);

        // Act
        var updated = metadata.IncrementAccess();

        // Assert
        Assert.Equal(11, updated.AccessCount);
        Assert.NotEqual(oldAccessTime, updated.LastAccessedAt);
        Assert.True(updated.LastAccessedAt > oldAccessTime);
    }

    #endregion

    #region Scenario Tests

    [Fact]
    public void ShouldNewMemoryCreation_WhenUsingScenario()
    {
        // Simulate creating a new memory entry
        var agentId = AgentId.From(Guid.NewGuid());
        var metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow,
            lastAccessedAt: null,
            source: "user_conversation",
            relevance: 0.95,
            tags: QuestionTechnicalUrgentTags,
            createdBy: agentId,
            accessCount: 0,
            customProperties: new Dictionary<string, string>
            {
                { "conversation_id", "conv_12345" },
                { "user_id", "user_789" }
            });

        // Assert initial state
        Assert.Equal(0, metadata.AccessCount);
        Assert.Null(metadata.LastAccessedAt);
        Assert.Equal(0.95, metadata.Relevance);
        Assert.Contains("urgent", metadata.Tags ?? []);
    }

    [Fact]
    public void ShouldFrequentlyAccessedMemory_WhenUsingScenario()
    {
        // Simulate a frequently accessed memory
        var metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow.AddDays(-30),
            lastAccessedAt: DateTime.UtcNow.AddMinutes(-5),
            source: "knowledge_base",
            relevance: 0.99,
            tags: CoreReferenceApiTags,
            accessCount: 150);

        // Simulate multiple accesses
        var current = metadata;
        for (int i = 0; i < 5; i++)
        {
            current = current.IncrementAccess();
        }

        // Assert
        Assert.Equal(155, current.AccessCount);
        Assert.True(current.LastAccessedAt > metadata.LastAccessedAt);
        Assert.Equal(metadata.Relevance, current.Relevance); // Relevance doesn't change
    }

    [Fact]
    public void ShouldMemoryWithDecayingRelevance_WhenUsingScenario()
    {
        // Simulate updating relevance based on age (done externally to the record)
        var createdAt = DateTime.UtcNow.AddDays(-90);
        var metadata = MemoryMetadata.Create(
            createdAt,
            DateTime.UtcNow.AddDays(-30),
            "old_document",
            0.8,
            accessCount: 5);

        // Calculate age-based relevance decay
        var ageInDays = (DateTime.UtcNow - createdAt).TotalDays;
        var decayFactor = Math.Max(0.5, 1.0 - (ageInDays / 365.0));
        var adjustedRelevance = metadata.Relevance * decayFactor;

        // Create updated metadata with decayed relevance
        var updated = metadata with { Relevance = adjustedRelevance };

        // Assert
        Assert.True(updated.Relevance < metadata.Relevance);
        Assert.True(updated.Relevance > 0.4);
    }

    [Fact]
    public void ShouldMemoryFiltering_WhenUsingScenario()
    {
        // Create various memory metadata for filtering
        var memories = new[]
        {
            MemoryMetadata.Create(DateTime.UtcNow.AddHours(-1), null, "chat", 0.9,
                RecentImportantTags),
            MemoryMetadata.Create(DateTime.UtcNow.AddDays(-1), null, "document", 0.7,
                ReferenceTags),
            MemoryMetadata.Create(DateTime.UtcNow.AddHours(-2), null, "chat", 0.85,
                RecentTags),
            MemoryMetadata.Create(DateTime.UtcNow.AddDays(-7), null, "api", 0.6,
                TechnicalReferenceTags)
        };

        // Filter by source
        var chatMemories = memories.Where(m => m.Source == "chat").ToList();
        Assert.Equal(2, chatMemories.Count);

        // Filter by relevance threshold
        var highRelevance = memories.Where(m => m.Relevance > 0.8).ToList();
        Assert.Equal(2, highRelevance.Count);

        // Filter by tags
        var recentMemories = memories.Where(m => m.Tags?.Contains("recent") == true).ToList();
        Assert.Equal(2, recentMemories.Count);

        // Filter by age
        var recentCutoff = DateTime.UtcNow.AddHours(-12);
        var veryRecent = memories.Where(m => m.CreatedAt > recentCutoff).ToList();
        Assert.Equal(2, veryRecent.Count);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldBeAllowed_WhenUsingEdgeCaseWithEmptySource()
    {
        // Act
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "",
            0.5);

        // Assert
        Assert.Empty(metadata.Source);
    }

    [Fact]
    public void ShouldRelevanceExtremes_WhenUsingEdgeCase()
    {
        // Test extreme relevance values
        var zeroRelevance = MemoryMetadata.Create(DateTime.UtcNow, null, "test", 0.0);
        var oneRelevance = MemoryMetadata.Create(DateTime.UtcNow, null, "test", 1.0);
        var negativeRelevance = MemoryMetadata.Create(DateTime.UtcNow, null, "test", -0.5);
        var highRelevance = MemoryMetadata.Create(DateTime.UtcNow, null, "test", 10.5);

        // Assert
        Assert.Equal(0.0, zeroRelevance.Relevance);
        Assert.Equal(1.0, oneRelevance.Relevance);
        Assert.Equal(-0.5, negativeRelevance.Relevance);
        Assert.Equal(10.5, highRelevance.Relevance);
    }

    [Fact]
    public void ShouldEmptyTagsArray_WhenUsingEdgeCase()
    {
        // Act
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "source",
            0.7,
            []);

        // Assert
        Assert.NotNull(metadata.Tags);
        Assert.Empty(metadata.Tags);
    }

    [Fact]
    public void ShouldTagsWithNullOrEmpty_WhenUsingEdgeCase()
    {
        // Act
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "source",
            0.8,
            ["valid", null!, "", "  ", "another"]);

        // Assert
        Assert.NotNull(metadata.Tags);
        Assert.Equal(5, metadata.Tags.Count);
        Assert.Null(metadata.Tags[1]);
        Assert.Empty(metadata.Tags[2]);
        Assert.Equal("  ", metadata.Tags[3]);
    }

    [Fact]
    public void ShouldVeryHighAccessCount_WhenUsingEdgeCase()
    {
        // Act
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            DateTime.UtcNow,
            "popular",
            0.99,
            accessCount: int.MaxValue - 1);

        // Act - Increment should work without overflow in normal usage
        var updated = metadata.IncrementAccess();

        // Assert
        Assert.Equal(int.MaxValue, updated.AccessCount);
    }

    [Fact]
    public void ShouldFutureDates_WhenUsingEdgeCase()
    {
        // Future dates should be allowed (though not recommended)
        var futureDate = DateTime.UtcNow.AddDays(365);
        var metadata = MemoryMetadata.Create(
            futureDate,
            futureDate.AddHours(1),
            "future",
            0.5);

        // Assert
        Assert.True(metadata.CreatedAt > DateTime.UtcNow);
        Assert.True(metadata.LastAccessedAt > metadata.CreatedAt);
    }

    [Fact]
    public void ShouldEmptyCustomProperties_WhenUsingEdgeCase()
    {
        // Act
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "source",
            0.7,
            customProperties: []);

        // Assert
        Assert.NotNull(metadata.CustomProperties);
        Assert.Empty(metadata.CustomProperties);
    }

    [Fact]
    public void ShouldCustomPropertiesWithSpecialKeys_WhenUsingEdgeCase()
    {
        // Act
        var metadata = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "source",
            0.8,
            customProperties: new Dictionary<string, string>
            {
                { "", "empty key" },
                { "key with spaces", "value with spaces" },
                { "key/with/slashes", "value" },
                { "key:with:colons", "value" },
                { "key.with.dots", "value" },
                { "UPPERCASE", "value" }
            });

        // Assert
        Assert.Equal(6, metadata.CustomProperties?.Count);
        Assert.Equal("empty key", metadata.CustomProperties?[""]);
        Assert.Equal("value with spaces", metadata.CustomProperties?["key with spaces"]);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldMemoryLifecycle_WhenUsingComplexScenario()
    {
        // Simulate complete memory lifecycle
        var agentId = AgentId.From(Guid.NewGuid());

        // 1. Create new memory
        var newMemory = MemoryMetadata.Create(
            DateTime.UtcNow,
            null,
            "user_query",
            0.9,
            NewUnprocessedTags,
            agentId);

        // 2. First access
        var firstAccess = newMemory.IncrementAccess();

        // 3. Update with processing results
        var processed = firstAccess with
        {
            Tags = ProcessedAnsweredCachedTags,
            CustomProperties = new Dictionary<string, string>
            {
                { "processing_time", "250ms" },
                { "response_quality", "high" }
            }
        };

        // 4. Multiple subsequent accesses
        var accessed = processed;
        for (int i = 0; i < 10; i++)
        {
            accessed = accessed.IncrementAccess();
        }

        // 5. Relevance boost due to frequent access
        var boostedRelevance = Math.Min(1.0, accessed.Relevance + (accessed.AccessCount * 0.001));
        var boosted = accessed with { Relevance = boostedRelevance };

        // Assert lifecycle progression
        Assert.Null(newMemory.LastAccessedAt);
        Assert.Equal(0, newMemory.AccessCount);

        Assert.NotNull(firstAccess.LastAccessedAt);
        Assert.Equal(1, firstAccess.AccessCount);

        Assert.Contains("cached", processed.Tags);
        Assert.NotNull(processed.CustomProperties);

        Assert.Equal(11, accessed.AccessCount);
        Assert.True(boosted.Relevance > newMemory.Relevance);
    }

    [Fact]
    public void ShouldMemoryComparison_WhenUsingComplexScenario()
    {
        // Create memories with different characteristics
        var now = DateTime.UtcNow;
        var agentId1 = AgentId.From(Guid.NewGuid());
        var agentId2 = AgentId.From(Guid.NewGuid());

        var memories = new[]
        {
            // High relevance, low access
            MemoryMetadata.Create(now.AddDays(-1), now.AddHours(-1), "critical", 0.99,
                ImportantTags, agentId1, 2),

            // Medium relevance, high access
            MemoryMetadata.Create(now.AddDays(-7), now.AddMinutes(-30), "common", 0.7,
                FrequentTags, agentId1, 50),

            // Low relevance, no access
            MemoryMetadata.Create(now.AddDays(-30), null, "archive", 0.3,
                OldTags, agentId2, 0),

            // Recent, medium relevance
            MemoryMetadata.Create(now.AddMinutes(-10), now.AddMinutes(-5), "recent", 0.8,
                NewTags, agentId2, 1)
        };

        // Sort by different criteria
        var byRelevance = memories.OrderByDescending(m => m.Relevance).ToList();
        var byAccess = memories.OrderByDescending(m => m.AccessCount).ToList();
        var byRecency = memories.OrderByDescending(m => m.CreatedAt).ToList();

        // Assert different orderings
        Assert.Equal("critical", byRelevance[0].Source);
        Assert.Equal("common", byAccess[0].Source);
        Assert.Equal("recent", byRecency[0].Source);

        // Calculate composite scores
        var withScores = memories.Select(m =>
        {
            var recencyScore = 1.0 / (1.0 + (now - m.CreatedAt).TotalDays);
            var accessScore = Math.Min(1.0, m.AccessCount / 50.0);
            var compositeScore = (m.Relevance * 0.5) + (recencyScore * 0.3) + (accessScore * 0.2);
            return (Memory: m, Score: compositeScore);
        }).OrderByDescending(x => x.Score).ToList();

        // Recent memory might rank high despite lower absolute relevance
        Assert.Contains(withScores, x => x.Memory.Source == "recent" && x.Score > 0.5);
    }

    #endregion
}
