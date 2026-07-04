using Orkeon.Domain.Agent;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Agent;

public class AgentMemoryTests
{
    [Fact]
    public void ShouldCreateShortTermMemory_WhenCreatingShortTermWithValidParameters()
    {
        // Arrange
        var content = "Important information about the task";
        var context = "During task execution";
        var relevanceScore = 0.6;

        // Act
        var memory = AgentMemory.CreateShortTerm(content, context, relevanceScore);

        // Assert
        Assert.NotNull(memory);
        Assert.NotNull(memory.Id);
        Assert.Equal(MemoryType.ShortTerm, memory.Type);
        Assert.Equal(content, memory.Content);
        Assert.Equal(context, memory.Context);
        Assert.Equal(relevanceScore, memory.RelevanceScore);
        Assert.Equal(0, memory.AccessCount);
        Assert.True(memory.CreatedAt <= DateTime.UtcNow);
        Assert.Equal(memory.CreatedAt, memory.LastAccessedAt);
    }

    [Fact]
    public void ShouldCreateLongTermMemory_WhenCreatingLongTermWithValidParameters()
    {
        // Arrange
        var content = "Core knowledge about the domain";
        var context = "Domain expertise";
        var relevanceScore = 0.9;

        // Act
        var memory = AgentMemory.CreateLongTerm(content, context, relevanceScore);

        // Assert
        Assert.NotNull(memory);
        Assert.Equal(MemoryType.LongTerm, memory.Type);
        Assert.Equal(content, memory.Content);
        Assert.Equal(context, memory.Context);
        Assert.Equal(relevanceScore, memory.RelevanceScore);
    }

    [Fact]
    public void ShouldCreateEpisodicMemory_WhenCreatingEpisodicWithValidParameters()
    {
        // Arrange
        var content = "Completed authentication task successfully";
        var context = "Task completion event";
        var relevanceScore = 0.75;

        // Act
        var memory = AgentMemory.CreateEpisodic(content, context, relevanceScore);

        // Assert
        Assert.NotNull(memory);
        Assert.Equal(MemoryType.Episodic, memory.Type);
        Assert.Equal(content, memory.Content);
        Assert.Equal(context, memory.Context);
        Assert.Equal(relevanceScore, memory.RelevanceScore);
    }

    [Fact]
    public void ShouldUseDefaults_WhenCreatingShortTermWithDefaultParameters()
    {
        // Arrange
        var content = "Basic information";

        // Act
        var memory = AgentMemory.CreateShortTerm(content);

        // Assert
        Assert.Equal(content, memory.Content);
        Assert.Null(memory.Context);
        Assert.Equal(0.5, memory.RelevanceScore); // Default for short-term
    }

    [Fact]
    public void ShouldUseHigherDefault_WhenCreatingLongTermWithDefaultRelevance()
    {
        // Arrange
        var content = "Important knowledge";

        // Act
        var memory = AgentMemory.CreateLongTerm(content);

        // Assert
        Assert.Equal(0.8, memory.RelevanceScore); // Default for long-term
    }

    [Fact]
    public void ShouldUseMediumDefault_WhenCreatingEpisodicWithDefaultRelevance()
    {
        // Arrange
        var content = "Event memory";

        // Act
        var memory = AgentMemory.CreateEpisodic(content);

        // Assert
        Assert.Equal(0.7, memory.RelevanceScore); // Default for episodic
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreatingWithNullContent()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => AgentMemory.CreateShortTerm(null!));
        Assert.Equal("content", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithNegativeRelevanceScore()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => AgentMemory.CreateShortTerm("content", relevanceScore: -0.1));
        Assert.Contains("Relevance score must be between 0 and 1", exception.Message);
        Assert.Equal("relevanceScore", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithRelevanceScoreAboveOne()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => AgentMemory.CreateShortTerm("content", relevanceScore: 1.1));
        Assert.Contains("Relevance score must be between 0 and 1", exception.Message);
        Assert.Equal("relevanceScore", exception.ParamName);
    }

    [Fact]
    public void ShouldIncrementAccessCount_WhenRecordingAccess()
    {
        // Arrange
        var memory = AgentMemory.CreateShortTerm("test content");
        var initialAccessCount = memory.AccessCount;
        var initialLastAccessed = memory.LastAccessedAt;

        // Wait until the clock has advanced past the captured timestamp (R5.6, deterministic)
        ClockAdvance.UntilStrictlyAfter(initialLastAccessed);

        // Act
        memory.RecordAccess();

        // Assert
        Assert.Equal(initialAccessCount + 1, memory.AccessCount);
        Assert.True(memory.LastAccessedAt > initialLastAccessed);
    }

    [Fact]
    public void ShouldIncrementCorrectly_WhenRecordingAccessWithMultipleTimes()
    {
        // Arrange
        var memory = AgentMemory.CreateShortTerm("test content");

        // Act
        memory.RecordAccess();
        memory.RecordAccess();
        memory.RecordAccess();

        // Assert
        Assert.Equal(3, memory.AccessCount);
    }

    [Fact]
    public void ShouldRetain_LongTermMemory_ShouldAlwaysReturnTrue()
    {
        // Arrange
        var memory = AgentMemory.CreateLongTerm("permanent knowledge");
        var maxAge = TimeSpan.FromMinutes(1); // Very short max age

        // Act
        var shouldRetain = memory.ShouldRetain(maxAge, minAccessCount: 100);

        // Assert
        Assert.True(shouldRetain); // Long-term memories are always retained
    }

    [Fact]
    public void ShouldRetain_FrequentlyAccessedMemory_ShouldReturnTrue()
    {
        // Arrange
        var memory = AgentMemory.CreateShortTerm("frequently used info");
        memory.RecordAccess();
        memory.RecordAccess();
        memory.RecordAccess();

        // Act
        var shouldRetain = memory.ShouldRetain(TimeSpan.FromHours(1), minAccessCount: 3);

        // Assert
        Assert.True(shouldRetain);
    }

    [Fact]
    public void ShouldRetain_RecentMemory_ShouldReturnTrue()
    {
        // Arrange
        var memory = AgentMemory.CreateShortTerm("recent info");

        // Act
        var shouldRetain = memory.ShouldRetain(TimeSpan.FromHours(24));

        // Assert
        Assert.True(shouldRetain); // Recent memory within max age
    }

    [Fact]
    public void ShouldRetain_HighlyRelevantMemory_ShouldReturnTrue()
    {
        // Arrange
        var memory = AgentMemory.CreateShortTerm("critical info", relevanceScore: 0.85);

        // Act
        var shouldRetain = memory.ShouldRetain(TimeSpan.Zero, minAccessCount: 100);

        // Assert
        Assert.True(shouldRetain); // High relevance (>= 0.8) always retained
    }

    [Fact]
    public void ShouldRetain_OldUnusedLowRelevanceMemory_ShouldReturnFalse()
    {
        // Arrange
        var memory = AgentMemory.CreateShortTerm("old info", relevanceScore: 0.3);
        // Note: We can't easily test with actual old memory due to DateTime.UtcNow
        // So we test with zero max age

        // Act
        var shouldRetain = memory.ShouldRetain(TimeSpan.Zero, minAccessCount: 5);

        // Assert
        Assert.False(shouldRetain);
    }

    [Fact]
    public void ShouldBeUnique_WhenUsingMemoryIds()
    {
        // Arrange & Act
        var memory1 = AgentMemory.CreateShortTerm("content 1");
        var memory2 = AgentMemory.CreateShortTerm("content 2");

        // Assert
        Assert.NotEqual(memory1.Id, memory2.Id);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ShouldAcceptValue_WhenCreatingWithValidRelevanceScore(double relevanceScore)
    {
        // Act
        var memory = AgentMemory.CreateShortTerm("content", relevanceScore: relevanceScore);

        // Assert
        Assert.Equal(relevanceScore, memory.RelevanceScore);
    }

    [Fact]
    public void ShouldBeAllowed_WhenCreatingWithEmptyContent()
    {
        // Act
        var memory = AgentMemory.CreateShortTerm("");

        // Assert
        Assert.Equal("", memory.Content);
    }

    [Fact]
    public void ShouldRetain_EpisodicMemory_WithMixedCriteria_ShouldEvaluateCorrectly()
    {
        // Arrange
        var memory = AgentMemory.CreateEpisodic("event data", relevanceScore: 0.6);
        memory.RecordAccess();
        memory.RecordAccess();

        // Act
        var shouldRetainDueToAccess = memory.ShouldRetain(TimeSpan.Zero, minAccessCount: 2);
        var shouldRetainDueToAge = memory.ShouldRetain(TimeSpan.FromDays(1), minAccessCount: 10);
        var shouldNotRetain = memory.ShouldRetain(TimeSpan.Zero, minAccessCount: 5);

        // Assert
        Assert.True(shouldRetainDueToAccess); // Has 2 accesses, meets minAccessCount
        Assert.True(shouldRetainDueToAge); // Is recent (within 1 day)
        Assert.False(shouldNotRetain); // Doesn't meet any criteria
    }
}
