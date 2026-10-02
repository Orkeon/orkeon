using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

/// <summary>
/// The consolidator clusters a crew's memories by similarity, merges a cluster through the LLM and
/// prunes low-value memories — in the memory it is given, the crew's (GAP-30): the merged memory is
/// embedded and added there, and what it replaces or prunes is removed there by the key that memory
/// returned.
/// </summary>
public class MemoryConsolidatorTests
{
    private static readonly float[] EmbeddingUnit3 = [1.0f, 0.0f, 0.0f];
    private static readonly float[] EmbeddingLow0 = [1, 0, 0, 0, 0];
    private static readonly float[] EmbeddingLow1 = [0, 1, 0, 0, 0];
    private static readonly float[] EmbeddingLow2 = [0, 0, 1, 0, 0];
    private static readonly float[] EmbeddingLow3 = [0, 0, 0, 1, 0];
    private static readonly float[] EmbeddingLow4 = [0, 0, 0, 0, 1];
    private static readonly float[] EmbeddingMixed = [0.5f, 0.5f, 0.5f];
    private static readonly float[] EmbeddingOrth6_0 = [1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f];
    private static readonly float[] EmbeddingOrth6_1 = [0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f];
    private static readonly float[] EmbeddingOrth6_2 = [0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f];
    private static readonly float[] EmbeddingOrth6_3 = [0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f];
    private static readonly float[] EmbeddingOrth6_4 = [0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f];
    private static readonly float[] EmbeddingOrth6_5 = [0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f];

    private readonly MockLlmProvider _llmProvider;
    private readonly MockEmbeddingProvider _embeddingProvider;
    private readonly RecordingLongTermMemory _memory = new();
    private readonly MemoryConsolidator _consolidator;
    private readonly CognitiveMemoryOptions _cognitiveOptions;

    public MemoryConsolidatorTests()
    {
        _llmProvider = new MockLlmProvider();
        _embeddingProvider = new MockEmbeddingProvider();
        _cognitiveOptions = new CognitiveMemoryOptions();
        var options = Options.Create(_cognitiveOptions);
        var logger = new MockLogger<MemoryConsolidator>();
        _consolidator = new MemoryConsolidator(_llmProvider, _embeddingProvider, options, logger);
    }

    /// <summary>The memories as a search of the crew's memory returns them: scored, keyed.</summary>
    private static List<ScoredMemoryItem> Found(IEnumerable<MemoryItem> items) =>
        [.. items.Select(item => new ScoredMemoryItem(item, 1f, $"key-{item.Id}"))];

    [Fact]
    public async Task ConsolidateAsync_FewItems_SkipsConsolidation()
    {
        // Arrange - fewer than 5 items
        var items = new List<MemoryItem>
        {
            MemoryItem.Create("Memory 1"),
            MemoryItem.Create("Memory 2"),
            MemoryItem.Create("Memory 3")
        };

        // Act
        var result = await _consolidator.ConsolidateAsync(Found(items), _memory, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.MergedCount);
        Assert.Equal(0, result.PrunedCount);
        Assert.Equal(3, result.UnchangedCount);
        Assert.Equal(0, _llmProvider.ChatCallCount);
    }

    // GAP-18: AnalysisModel unset merges on the provider's own model, not OpenAI's.
    [Fact]
    public async Task ConsolidateAsync_WithoutAnAnalysisModel_MergesOnTheProvidersOwnModel()
    {
        var items = new List<MemoryItem>();
        for (var i = 0; i < 5; i++)
            items.Add(MemoryItem.Create($"Similar content {i}", embedding: EmbeddingUnit3, importance: 0.5f));
        _llmProvider.SetChatResult("Merged: All similar content combined");

        await _consolidator.ConsolidateAsync(Found(items), _memory, CancellationToken.None);

        Assert.Equal(string.Empty, _llmProvider.LastChatConfig!.Model);
    }

    [Fact]
    public async Task ConsolidateAsync_MergesRedundantCluster_into_one_embedded_memory_and_removes_what_it_replaces()
    {
        // Arrange - create items with identical embeddings (will cluster together)
        var items = new List<MemoryItem>();
        for (var i = 0; i < 5; i++)
            items.Add(MemoryItem.Create($"Similar content {i}", embedding: EmbeddingUnit3, importance: 0.5f));

        _llmProvider.SetChatResult("Merged: All similar content combined");
        _embeddingProvider.SetEmbeddingResult(EmbeddingMixed);

        // Act
        var result = await _consolidator.ConsolidateAsync(Found(items), _memory, CancellationToken.None);

        // Assert
        Assert.Equal(5, result.MergedCount);
        var merged = Assert.Single(_memory.Added);
        Assert.Equal("Merged: All similar content combined", merged.Content);
        Assert.Equal(EmbeddingMixed, merged.Embedding);
        Assert.Equal(merged.Id.ToString(), Assert.Single(result.CreatedMemoryIds));
        Assert.Equal(items.Select(item => $"key-{item.Id}"), _memory.Removed);
    }

    [Fact]
    public async Task ConsolidateAsync_PrunesOldLowImportance()
    {
        // Arrange - items with low importance and orthogonal embeddings (no clustering)
        var items = new List<MemoryItem>
        {
            MemoryItem.Create("Low 0", embedding: EmbeddingLow0, importance: 0.05f),
            MemoryItem.Create("Low 1", embedding: EmbeddingLow1, importance: 0.05f),
            MemoryItem.Create("Low 2", embedding: EmbeddingLow2, importance: 0.05f),
            MemoryItem.Create("Low 3", embedding: EmbeddingLow3, importance: 0.05f),
            MemoryItem.Create("Low 4", embedding: EmbeddingLow4, importance: 0.05f),
        };

        // Override pruning settings to prune recent items too
        var opts = new CognitiveMemoryOptions { PruningMinAgeDays = 0 };
        var consolidator = new MemoryConsolidator(
            _llmProvider, _embeddingProvider, Options.Create(opts),
            new MockLogger<MemoryConsolidator>());

        // Act
        var result = await consolidator.ConsolidateAsync(Found(items), _memory, CancellationToken.None);

        // Assert
        Assert.Equal(5, result.PrunedCount);
        Assert.Equal(items.Select(item => $"key-{item.Id}"), _memory.Removed);
    }

    [Fact]
    public async Task ConsolidateAsync_CreatesNewMergedItems()
    {
        // Arrange - two pairs of similar items + one unique
        var embeddingA = new float[] { 1.0f, 0.0f, 0.0f };
        var embeddingB = new float[] { 0.0f, 1.0f, 0.0f };
        var items = new List<MemoryItem>
        {
            MemoryItem.Create("Topic A version 1", embedding: embeddingA, importance: 0.6f),
            MemoryItem.Create("Topic A version 2", embedding: embeddingA, importance: 0.7f),
            MemoryItem.Create("Topic B version 1", embedding: embeddingB, importance: 0.5f),
            MemoryItem.Create("Topic B version 2", embedding: embeddingB, importance: 0.8f),
            MemoryItem.Create("Unique topic", embedding: EmbeddingMixed, importance: 0.9f)
        };

        _llmProvider.SetChatResult("Merged content from cluster");

        // Act
        var result = await _consolidator.ConsolidateAsync(Found(items), _memory, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.CreatedMemoryIds.Count);
        Assert.Equal(2, _memory.Added.Count);
        Assert.Equal(4, _memory.Removed.Count);
    }

    [Fact]
    public async Task ConsolidateAsync_ReturnsCorrectCounts()
    {
        // Arrange - all items with orthogonal embeddings (no clusters), high importance (no pruning)
        var items = new List<MemoryItem>
        {
            MemoryItem.Create("Unique 0", embedding: EmbeddingOrth6_0, importance: 0.9f),
            MemoryItem.Create("Unique 1", embedding: EmbeddingOrth6_1, importance: 0.9f),
            MemoryItem.Create("Unique 2", embedding: EmbeddingOrth6_2, importance: 0.9f),
            MemoryItem.Create("Unique 3", embedding: EmbeddingOrth6_3, importance: 0.9f),
            MemoryItem.Create("Unique 4", embedding: EmbeddingOrth6_4, importance: 0.9f),
            MemoryItem.Create("Unique 5", embedding: EmbeddingOrth6_5, importance: 0.9f),
        };

        // Act
        var result = await _consolidator.ConsolidateAsync(Found(items), _memory, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.MergedCount);
        Assert.Equal(0, result.PrunedCount);
        Assert.Equal(6, result.UnchangedCount);
        Assert.Empty(_memory.Removed);
    }
}
