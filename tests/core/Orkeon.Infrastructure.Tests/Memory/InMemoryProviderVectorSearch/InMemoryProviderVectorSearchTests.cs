using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.Memory;

public class InMemoryProviderVectorSearchTests
{
    private static readonly float[] s_unitX = [1f, 0f, 0f];
    private static readonly float[] s_unitY = [0f, 1f, 0f];
    private static readonly float[] s_negUnitX = [-1f, 0f, 0f];
    private static readonly float[] s_high = [0.95f, 0.05f, 0f];
    private static readonly float[] s_medium = [0.5f, 0.5f, 0.5f];
    private static readonly float[] s_vector0102_03 = [0.1f, 0.2f, 0.3f];
    private static readonly float[] s_nearX = [0.9f, 0.1f, 0f];
    private static readonly float[] s_low = [0.1f, 0.9f, 0f];
    private static readonly float[] s_midHigh = [0.7f, 0.3f, 0f];
    private static readonly float[] s_nearX99 = [0.99f, 0.01f, 0f];
    private static readonly float[] s_vector2D_10 = [1f, 0f];

    private readonly InMemoryProvider _provider;

    public InMemoryProviderVectorSearchTests()
    {
        _provider = new InMemoryProvider(new TestLogger<InMemoryProvider>());
    }

    [Fact]
    public async Task ShouldReturnTopKResults_WhenSearchSimilarAsync()
    {
        // Arrange - store 5 items with embeddings, search for top 3
        var queryEmbedding = s_unitX;

        await _provider.StoreWithEmbeddingAsync("k1",
            MemoryItem.Create("item1"), s_unitX, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k2",
            MemoryItem.Create("item2"), s_nearX, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k3",
            MemoryItem.Create("item3"), s_medium, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k4",
            MemoryItem.Create("item4"), s_unitY, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k5",
            MemoryItem.Create("item5"), s_negUnitX, TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchSimilarAsync(queryEmbedding, topK: 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("item1", results[0].Item.Content); // Exact match, score = 1.0
    }

    [Fact]
    public async Task ShouldFilterMinScore_WhenSearchSimilarAsync()
    {
        // Arrange
        var queryEmbedding = s_unitX;

        await _provider.StoreWithEmbeddingAsync("k1",
            MemoryItem.Create("high"), s_high, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k2",
            MemoryItem.Create("medium"), s_medium, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k3",
            MemoryItem.Create("low"), s_unitY, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k4",
            MemoryItem.Create("opposite"), s_negUnitX, TestContext.Current.CancellationToken);

        // Act - high min score threshold
        var results = await _provider.SearchSimilarAsync(queryEmbedding, topK: 10, minScore: 0.9f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - only the high-similarity item passes
        Assert.Single(results);
        Assert.Equal("high", results[0].Item.Content);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchSimilarAsyncNoEmbeddings()
    {
        // Arrange - store items without embeddings
        await _provider.StoreAsync("k1", MemoryItem.Create("no embedding 1"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("k2", MemoryItem.Create("no embedding 2"), TestContext.Current.CancellationToken);

        var queryEmbedding = s_unitX;

        // Act
        var results = await _provider.SearchSimilarAsync(queryEmbedding, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldStoreEmbedding_WhenStoreWithEmbeddingAsync()
    {
        // Arrange
        var embedding = s_vector0102_03;

        // Act
        await _provider.StoreWithEmbeddingAsync("key1", MemoryItem.Create("content"), embedding, TestContext.Current.CancellationToken);

        // Assert
        var retrieved = await _provider.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Embedding);
        Assert.Equal(embedding, retrieved.Embedding);
    }

    [Fact]
    public async Task ShouldSortedByScoreDescending_WhenSearchSimilarAsync()
    {
        // Arrange
        var queryEmbedding = s_unitX;

        // Store in non-sorted order
        await _provider.StoreWithEmbeddingAsync("k1",
            MemoryItem.Create("low"), s_low, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k2",
            MemoryItem.Create("high"), s_nearX99, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k3",
            MemoryItem.Create("medium"), s_midHigh, TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchSimilarAsync(queryEmbedding, topK: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - verify descending order
        Assert.Equal(3, results.Count);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.True(results[1].Score >= results[2].Score);
        Assert.Equal("high", results[0].Item.Content);
    }

    [Fact]
    public async Task ShouldMixedEmbeddingsAndNoEmbeddingsOnlySearchesEmbedded_WhenSearchSimilarAsync()
    {
        // Arrange
        var queryEmbedding = s_unitX;

        await _provider.StoreAsync("k1", MemoryItem.Create("no embedding"), TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k2",
            MemoryItem.Create("with embedding"), s_unitX, TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchSimilarAsync(queryEmbedding, topK: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - only the embedded item appears
        Assert.Single(results);
        Assert.Equal("with embedding", results[0].Item.Content);
    }

    [Fact]
    public async Task ShouldSkipItem_WhenSearchSimilarAsyncDimensionMismatch()
    {
        // Arrange
        var queryEmbedding = s_unitX;

        // Store an item with different dimension
        await _provider.StoreWithEmbeddingAsync("k1",
            MemoryItem.Create("wrong dim"), s_vector2D_10, TestContext.Current.CancellationToken);
        await _provider.StoreWithEmbeddingAsync("k2",
            MemoryItem.Create("right dim"), s_unitX, TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchSimilarAsync(queryEmbedding, topK: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - only the matching dimension item appears
        Assert.Single(results);
        Assert.Equal("right dim", results[0].Item.Content);
    }

    [Fact]
    public async Task ShouldExactMatchScoreCloseToOne_WhenSearchSimilarAsync()
    {
        // Arrange
        var embedding = s_medium;
        await _provider.StoreWithEmbeddingAsync("k1",
            MemoryItem.Create("exact"), embedding, TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchSimilarAsync(embedding, topK: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.Equal(1f, results[0].Score, precision: 4);
    }
}
