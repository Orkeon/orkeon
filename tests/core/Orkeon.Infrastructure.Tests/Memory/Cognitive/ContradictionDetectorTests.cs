using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

public class ContradictionDetectorTests
{
    private readonly MockLlmProvider _llmProvider;
    private readonly ContradictionDetector _detector;

    public ContradictionDetectorTests()
    {
        _llmProvider = new MockLlmProvider();
        var options = Options.Create(new CognitiveMemoryOptions());
        var logger = new MockLogger<ContradictionDetector>();
        _detector = new ContradictionDetector(_llmProvider, options, logger);
    }

    [Fact]
    public async Task CheckAsync_NoContradiction_ReturnsFalse()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"has_contradiction": false, "conflicting_ids": [], "description": "", "resolution": "", "action": "keep_both"}
            """);

        var existing = new List<MemoryItem>
        {
            MemoryItem.Create("The sky is blue", importance: 0.5f)
        };

        // Act
        var result = await _detector.CheckAsync("Water is wet", existing, CancellationToken.None);

        // Assert
        Assert.False(result.HasContradiction);
        Assert.Empty(result.ConflictingMemoryIds);
    }

    [Fact]
    public async Task CheckAsync_Contradiction_ReturnsTrue()
    {
        // Arrange
        var existingItem = MemoryItem.Create("The meeting is on Monday", importance: 0.7f);
        _llmProvider.SetChatResult($$"""
            {"has_contradiction": true, "conflicting_ids": ["{{existingItem.Id}}"], "description": "Meeting day conflict", "resolution": "Use the updated date", "action": "keep_new"}
            """);

        var existing = new List<MemoryItem> { existingItem };

        // Act
        var result = await _detector.CheckAsync("The meeting is on Tuesday", existing, CancellationToken.None);

        // Assert
        Assert.True(result.HasContradiction);
        Assert.Contains(existingItem.Id, result.ConflictingMemoryIds);
        Assert.Equal(ConflictResolution.KeepNew, result.RecommendedAction);
    }

    [Fact]
    public async Task CheckAsync_Merge_RecommendsMerge()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"has_contradiction": true, "conflicting_ids": ["id1"], "description": "Overlapping info", "resolution": "Combine both", "action": "merge"}
            """);

        var existing = new List<MemoryItem>
        {
            MemoryItem.Create("Partial information about X", importance: 0.5f)
        };

        // Act
        var result = await _detector.CheckAsync("More information about X", existing, CancellationToken.None);

        // Assert
        Assert.True(result.HasContradiction);
        Assert.Equal(ConflictResolution.Merge, result.RecommendedAction);
    }

    [Fact]
    public async Task CheckAsync_KeepBoth_WhenNoRealConflict()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"has_contradiction": false, "conflicting_ids": [], "description": "", "resolution": "", "action": "keep_both"}
            """);

        var existing = new List<MemoryItem>
        {
            MemoryItem.Create("Alice likes coffee", importance: 0.3f)
        };

        // Act
        var result = await _detector.CheckAsync("Bob likes tea", existing, CancellationToken.None);

        // Assert
        Assert.False(result.HasContradiction);
        Assert.Equal(ConflictResolution.KeepBoth, result.RecommendedAction);
    }

    [Fact]
    public async Task CheckAsync_EmptyMemories_ReturnsFalse()
    {
        // Act
        var result = await _detector.CheckAsync("Some new content", [], CancellationToken.None);

        // Assert
        Assert.False(result.HasContradiction);
        Assert.Equal(0, _llmProvider.ChatCallCount); // Should not call LLM for empty memories
    }
}
