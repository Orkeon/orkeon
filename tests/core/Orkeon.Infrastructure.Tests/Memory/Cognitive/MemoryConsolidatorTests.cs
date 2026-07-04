using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

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
    private readonly MockMemoryProvider _memoryProvider;
    private readonly MemoryConsolidator _consolidator;
    private readonly CognitiveMemoryOptions _cognitiveOptions;

    public MemoryConsolidatorTests()
    {
        _llmProvider = new MockLlmProvider();
        _memoryProvider = new MockMemoryProvider();
        _cognitiveOptions = new CognitiveMemoryOptions();
        var options = Options.Create(_cognitiveOptions);
        var logger = new MockLogger<MemoryConsolidator>();
        _consolidator = new MemoryConsolidator(_llmProvider, _memoryProvider, options, logger);
    }

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
        var result = await _consolidator.ConsolidateAsync(items, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.MergedCount);
        Assert.Equal(0, result.PrunedCount);
        Assert.Equal(3, result.UnchangedCount);
        Assert.Equal(0, _llmProvider.ChatCallCount);
    }

    [Fact]
    public async Task ConsolidateAsync_MergesRedundantCluster()
    {
        // Arrange - create items with identical embeddings (will cluster together)
        var embedding = EmbeddingUnit3;
        var items = new List<MemoryItem>();

        for (var i = 0; i < 5; i++)
        {
            var item = MemoryItem.Create($"Similar content {i}", embedding: embedding, importance: 0.5f);
            items.Add(item);
            await _memoryProvider.StoreAsync(item.Id, item, TestContext.Current.CancellationToken);
        }

        _llmProvider.SetChatResult("Merged: All similar content combined");

        // Act
        var result = await _consolidator.ConsolidateAsync(items, CancellationToken.None);

        // Assert
        Assert.True(result.MergedCount > 0);
        Assert.NotEmpty(result.CreatedMemoryIds);
        Assert.True(_llmProvider.ChatCallCount > 0);
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

        foreach (var item in items)
            await _memoryProvider.StoreAsync(item.Id, item, TestContext.Current.CancellationToken);

        // Override pruning settings to prune recent items too
        var opts = new CognitiveMemoryOptions { PruningMinAgeDays = 0 };
        var consolidator = new MemoryConsolidator(
            _llmProvider, _memoryProvider, Options.Create(opts),
            new MockLogger<MemoryConsolidator>());

        // Act
        var result = await consolidator.ConsolidateAsync(items, CancellationToken.None);

        // Assert
        Assert.True(result.PrunedCount > 0);
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

        foreach (var item in items)
            await _memoryProvider.StoreAsync(item.Id, item, TestContext.Current.CancellationToken);

        _llmProvider.SetChatResult("Merged content from cluster");

        // Act
        var result = await _consolidator.ConsolidateAsync(items, CancellationToken.None);

        // Assert
        Assert.NotEmpty(result.CreatedMemoryIds);
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
        var result = await _consolidator.ConsolidateAsync(items, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.MergedCount);
        Assert.Equal(0, result.PrunedCount);
        Assert.Equal(6, result.UnchangedCount);
    }
}
