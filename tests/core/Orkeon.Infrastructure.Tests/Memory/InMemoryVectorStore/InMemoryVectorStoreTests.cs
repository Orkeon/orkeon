using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Memory;

public class InMemoryVectorStoreTests
{
    private static readonly float[] s_vector3D_0102_03 = [0.1f, 0.2f, 0.3f];
    private static readonly float[] s_vector3D_050505 = [0.5f, 0.5f, 0.5f];
    private static readonly float[] s_vector3D_100000 = [1.0f, 0.0f, 0.0f];
    private static readonly float[] s_vector3D_001000 = [0.0f, 1.0f, 0.0f];
    private static readonly float[] s_vector3D_neg100 = [-1.0f, 0.0f, 0.0f];
    private static readonly float[] s_vector3D_090100 = [0.9f, 0.1f, 0.0f];
    private static readonly float[] s_vector3D_010900 = [0.1f, 0.9f, 0.0f];
    private static readonly float[] s_vector3D_040506 = [0.4f, 0.5f, 0.6f];
    private static readonly float[] s_vector2D_1000 = [1.0f, 0.0f];
    private static readonly float[] s_vector4D_10000000 = [1.0f, 0.0f, 0.0f, 0.0f];
    private static readonly float[] s_vector3D_070700 = [0.7f, 0.7f, 0.0f];
    private static readonly float[] s_vector3D_050500 = [0.5f, 0.5f, 0.0f];
    private static readonly float[] s_vector3D_101000 = [1.0f, 1.0f, 0.0f];

    #region Constructor Tests

    [Fact]
    public void ShouldCreateVectorStore_WhenConstructorWithDefaultParameters()
    {
        // Act
        using var vectorStore = new InMemoryVectorStore();

        // Assert
        Assert.NotNull(vectorStore);
    }

    #endregion

    #region StoreAsync Tests

    [Fact]
    public async Task ShouldStoreSuccessfully_WhenStoreAsyncWithValidData()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem = CreateMemoryItem(TestContent);
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act
        await vectorStore.StoreAsync("test-id", memoryItem, vector, TestContext.Current.CancellationToken);

        // Assert
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count);

        var retrievedItem = await vectorStore.GetAsync("test-id", TestContext.Current.CancellationToken);
        Assert.NotNull(retrievedItem);
        Assert.Equal(TestContent, retrievedItem.Content);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenStoreAsyncWithNullId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem = CreateMemoryItem(TestContent);
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vectorStore.StoreAsync(null!, memoryItem, vector, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenStoreAsyncWithEmptyId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem = CreateMemoryItem(TestContent);
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => vectorStore.StoreAsync(string.Empty, memoryItem, vector, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenStoreAsyncWithNullMemoryItem()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vectorStore.StoreAsync("test-id", null!, vector, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenStoreAsyncWithNullVector()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem = CreateMemoryItem(TestContent);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vectorStore.StoreAsync("test-id", memoryItem, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldOverwriteExisting_WhenStoreAsyncWithDuplicateId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var originalItem = CreateMemoryItem("Original content");
        var newItem = CreateMemoryItem("New content");
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act
        await vectorStore.StoreAsync("test-id", originalItem, vector, TestContext.Current.CancellationToken);
        await vectorStore.StoreAsync("test-id", newItem, vector, TestContext.Current.CancellationToken);

        // Assert
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count);

        var retrievedItem = await vectorStore.GetAsync("test-id", TestContext.Current.CancellationToken);
        Assert.NotNull(retrievedItem);
        Assert.Equal("New content", retrievedItem.Content);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenStoreAsyncConcurrentStores()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 50; i++)
        {
            int itemIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                var memoryItem = CreateMemoryItem($"Content {itemIndex}");
                var vector = CreateEmbeddingVector([itemIndex * 0.1f, itemIndex * 0.2f, itemIndex * 0.3f]);
                await vectorStore.StoreAsync($"item-{itemIndex}", memoryItem, vector);
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(50, count);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task ShouldReturnItem_WhenGetAsyncWithExistingId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem = CreateMemoryItem(TestContent);
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);
        await vectorStore.StoreAsync("test-id", memoryItem, vector, TestContext.Current.CancellationToken);

        // Act
        var result = await vectorStore.GetAsync("test-id", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContent, result.Content);
        Assert.Equal(memoryItem.Timestamp, result.Timestamp);
    }

    [Fact]
    public async Task ShouldReturnNull_WhenGetAsyncWithNonExistentId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act
        var result = await vectorStore.GetAsync("non-existent-id", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenGetAsyncWithNullId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vectorStore.GetAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenGetAsyncWithEmptyId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => vectorStore.GetAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenGetAsyncConcurrentGets()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem = CreateMemoryItem("Shared content");
        var vector = CreateEmbeddingVector(s_vector3D_050505);
        await vectorStore.StoreAsync("shared-id", memoryItem, vector, TestContext.Current.CancellationToken);

        var tasks = new List<Task<MemoryItem?>>();

        // Act
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () => await vectorStore.GetAsync("shared-id")));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result =>
        {
            Assert.NotNull(result);
            Assert.Equal("Shared content", result.Content);
        });
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task ShouldReturnTrueAndRemoveItem_WhenRemoveAsyncWithExistingId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem = CreateMemoryItem(TestContent);
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);
        await vectorStore.StoreAsync("test-id", memoryItem, vector, TestContext.Current.CancellationToken);

        // Act
        var removed = await vectorStore.RemoveAsync("test-id", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(removed);
        var retrievedItem = await vectorStore.GetAsync("test-id", TestContext.Current.CancellationToken);
        Assert.Null(retrievedItem);
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenRemoveAsyncWithNonExistentId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act
        var removed = await vectorStore.RemoveAsync("non-existent-id", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenRemoveAsyncWithNullId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vectorStore.RemoveAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenRemoveAsyncWithEmptyId()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => vectorStore.RemoveAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenRemoveAsyncConcurrentRemovals()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Store multiple items
        for (int i = 0; i < 10; i++)
        {
            var memoryItem = CreateMemoryItem($"Content {i}");
            var vector = CreateEmbeddingVector([i * 0.1f, i * 0.2f, i * 0.3f]);
            await vectorStore.StoreAsync($"item-{i}", memoryItem, vector, TestContext.Current.CancellationToken);
        }

        var tasks = new List<Task<bool>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            int itemIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () => await vectorStore.RemoveAsync($"item-{itemIndex}")));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result => Assert.True(result));
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task ShouldReturnZero_WhenCountAsyncEmptyStore()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ShouldReturnCorrectCount_WhenCountAsyncWithItems()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        for (int i = 0; i < 5; i++)
        {
            var memoryItem = CreateMemoryItem($"Content {i}");
            var vector = CreateEmbeddingVector([i * 0.1f, i * 0.2f, i * 0.3f]);
            await vectorStore.StoreAsync($"item-{i}", memoryItem, vector, TestContext.Current.CancellationToken);
        }

        // Act
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task ShouldReturnCorrectCount_WhenCountAsyncAfterAddingAndRemoving()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var memoryItem1 = CreateMemoryItem("Content 1");
        var memoryItem2 = CreateMemoryItem("Content 2");
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);

        await vectorStore.StoreAsync("item-1", memoryItem1, vector, TestContext.Current.CancellationToken);
        await vectorStore.StoreAsync("item-2", memoryItem2, vector, TestContext.Current.CancellationToken);

        // Act
        var countAfterAdding = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        await vectorStore.RemoveAsync("item-1", TestContext.Current.CancellationToken);
        var countAfterRemoving = await vectorStore.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, countAfterAdding);
        Assert.Equal(1, countAfterRemoving);
    }

    #endregion

    #region ClearAsync Tests

    [Fact]
    public async Task ShouldNotThrow_WhenClearAsyncEmptyStore()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act & Assert - Should not throw
        await vectorStore.ClearAsync(TestContext.Current.CancellationToken);

        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ShouldRemoveAllItems_WhenClearAsyncWithItems()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        for (int i = 0; i < 5; i++)
        {
            var memoryItem = CreateMemoryItem($"Content {i}");
            var vector = CreateEmbeddingVector([i * 0.1f, i * 0.2f, i * 0.3f]);
            await vectorStore.StoreAsync($"item-{i}", memoryItem, vector, TestContext.Current.CancellationToken);
        }

        // Act
        await vectorStore.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);

        // Verify items are actually gone
        for (int i = 0; i < 5; i++)
        {
            var item = await vectorStore.GetAsync($"item-{i}", TestContext.Current.CancellationToken);
            Assert.Null(item);
        }
    }

    [Fact]
    public async Task ShouldAllowNewItems_WhenClearAsyncAfterClear()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var oldItem = CreateMemoryItem("Old content");
        var oldVector = CreateEmbeddingVector(s_vector3D_0102_03);
        await vectorStore.StoreAsync("old-item", oldItem, oldVector, TestContext.Current.CancellationToken);

        await vectorStore.ClearAsync(TestContext.Current.CancellationToken);

        // Act
        var newItem = CreateMemoryItem("New content");
        var newVector = CreateEmbeddingVector(s_vector3D_040506);
        await vectorStore.StoreAsync("new-item", newItem, newVector, TestContext.Current.CancellationToken);

        // Assert
        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count);

        var retrievedItem = await vectorStore.GetAsync("new-item", TestContext.Current.CancellationToken);
        Assert.NotNull(retrievedItem);
        Assert.Equal("New content", retrievedItem.Content);
    }

    #endregion

    #region SearchSimilarAsync Tests

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchSimilarAsyncEmptyStore()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var queryVector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act
        var results = await vectorStore.SearchSimilarAsync(queryVector, 10, 0.0f, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSearchSimilarAsyncWithNullQuery()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vectorStore.SearchSimilarAsync(null!, 10, 0.0f, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenSearchSimilarAsyncWithZeroLimit()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var queryVector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => vectorStore.SearchSimilarAsync(queryVector, 0, 0.0f, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenSearchSimilarAsyncWithNegativeLimit()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var queryVector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => vectorStore.SearchSimilarAsync(queryVector, -1, 0.0f, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenSearchSimilarAsyncWithInvalidThreshold()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var queryVector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => vectorStore.SearchSimilarAsync(queryVector, 10, -0.1f, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => vectorStore.SearchSimilarAsync(queryVector, 10, 1.1f, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnPerfectMatch_WhenSearchSimilarAsyncWithIdenticalVector()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);
        var memoryItem = CreateMemoryItem(TestContent);
        await vectorStore.StoreAsync("test-id", memoryItem, vector, TestContext.Current.CancellationToken);

        var queryVector = CreateEmbeddingVector(s_vector3D_0102_03);

        // Act
        var results = await vectorStore.SearchSimilarAsync(queryVector, 10, 0.0f, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.Equal(TestContent, results.First().Content);
    }

    [Fact]
    public async Task ShouldReturnOrderedBySimilarity_WhenSearchSimilarAsyncWithSimilarVectors()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Perfect match
        var perfectVector = CreateEmbeddingVector(s_vector3D_100000);
        var perfectItem = CreateMemoryItem("Perfect match");
        await vectorStore.StoreAsync("perfect", perfectItem, perfectVector, TestContext.Current.CancellationToken);

        // Good match (orthogonal)
        var goodVector = CreateEmbeddingVector(s_vector3D_001000);
        var goodItem = CreateMemoryItem("Good match");
        await vectorStore.StoreAsync("good", goodItem, goodVector, TestContext.Current.CancellationToken);

        // Opposite (negative correlation)
        var oppositeVector = CreateEmbeddingVector(s_vector3D_neg100);
        var oppositeItem = CreateMemoryItem("Opposite match");
        await vectorStore.StoreAsync("opposite", oppositeItem, oppositeVector, TestContext.Current.CancellationToken);

        var queryVector = CreateEmbeddingVector(s_vector3D_100000);

        // Act
        var results = await vectorStore.SearchSimilarAsync(queryVector, 10, 0.0f, TestContext.Current.CancellationToken);

        // Assert
        // With threshold 0.0f, only vectors with similarity >= 0 should be returned
        // Perfect match: similarity = 1.0 (included)
        // Good match (orthogonal): similarity = 0.0 (included)
        // Opposite: similarity = -1.0 (excluded)
        Assert.Equal(2, results.Count());
        var resultsList = results.ToList();

        // Should be ordered by similarity (perfect match first)
        Assert.Equal("Perfect match", resultsList[0].Content);
        Assert.Equal("Good match", resultsList[1].Content);
    }

    [Fact]
    public async Task ShouldFilterResults_WhenSearchSimilarAsyncWithThreshold()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // High similarity
        var highSimilarityVector = CreateEmbeddingVector(s_vector3D_090100);
        var highSimilarityItem = CreateMemoryItem("High similarity");
        await vectorStore.StoreAsync("high", highSimilarityItem, highSimilarityVector, TestContext.Current.CancellationToken);

        // Low similarity
        var lowSimilarityVector = CreateEmbeddingVector(s_vector3D_010900);
        var lowSimilarityItem = CreateMemoryItem("Low similarity");
        await vectorStore.StoreAsync("low", lowSimilarityItem, lowSimilarityVector, TestContext.Current.CancellationToken);

        var queryVector = CreateEmbeddingVector(s_vector3D_100000);

        // Act
        var resultsWithLowThreshold = await vectorStore.SearchSimilarAsync(queryVector, 10, 0.1f, TestContext.Current.CancellationToken);
        var resultsWithHighThreshold = await vectorStore.SearchSimilarAsync(queryVector, 10, 0.8f, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, resultsWithLowThreshold.Count()); // Both should pass low threshold
        Assert.Single(resultsWithHighThreshold); // Only high similarity should pass
        Assert.Equal("High similarity", resultsWithHighThreshold.First().Content);
    }

    [Fact]
    public async Task ShouldLimitResults_WhenSearchSimilarAsyncWithLimit()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        for (int i = 0; i < 10; i++)
        {
            var vector = CreateEmbeddingVector([1.0f, i * 0.1f, 0.0f]);
            var memoryItem = CreateMemoryItem($"Content {i}");
            await vectorStore.StoreAsync($"item-{i}", memoryItem, vector, TestContext.Current.CancellationToken);
        }

        var queryVector = CreateEmbeddingVector(s_vector3D_100000);

        // Act
        var results = await vectorStore.SearchSimilarAsync(queryVector, 5, 0.0f, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task ShouldIgnoreIncompatibleVectors_WhenSearchSimilarAsyncWithDifferentDimensions()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // 3D vector - will have similarity 1.0 with query
        var vector3D = CreateEmbeddingVector(s_vector3D_100000);
        var item3D = CreateMemoryItem("3D content");
        await vectorStore.StoreAsync("3d-item", item3D, vector3D, TestContext.Current.CancellationToken);

        // 2D vector - will have similarity 0 with 3D query due to dimension mismatch
        var vector2D = CreateEmbeddingVector(s_vector2D_1000);
        var item2D = CreateMemoryItem("2D content");
        await vectorStore.StoreAsync("2d-item", item2D, vector2D, TestContext.Current.CancellationToken);

        // 4D vector - will have similarity 0 with 3D query due to dimension mismatch
        var vector4D = CreateEmbeddingVector(s_vector4D_10000000);
        var item4D = CreateMemoryItem("4D content");
        await vectorStore.StoreAsync("4d-item", item4D, vector4D, TestContext.Current.CancellationToken);

        var queryVector = CreateEmbeddingVector(s_vector3D_100000); // 3D query

        // Act - threshold 0.5 ensures only high similarity matches (the 3D vector)
        var results = await vectorStore.SearchSimilarAsync(queryVector, 10, 0.5f, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results); // Only the 3D vector should match
        Assert.Equal("3D content", results.First().Content);
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenSearchSimilarAsyncCancellationToken()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var vector = CreateEmbeddingVector(s_vector3D_0102_03);
        var memoryItem = CreateMemoryItem(TestContent);
        await vectorStore.StoreAsync("test-id", memoryItem, vector, TestContext.Current.CancellationToken);

        var queryVector = CreateEmbeddingVector(s_vector3D_0102_03);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => vectorStore.SearchSimilarAsync(queryVector, 10, 0.0f, cts.Token));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ShouldWorkCorrectly_WhenIntegrationTestCompleteWorkflow()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();

        // Act & Assert - Store multiple items
        var items = new[]
        {
            (Id: "doc1", Content: "Document about cats", Vector: s_vector3D_100000),
            (Id: "doc2", Content: "Document about dogs", Vector: s_vector3D_001000),
            (Id: "doc3", Content: "Document about pets", Vector: s_vector3D_070700)
        };

        foreach (var (id, content, vectorValues) in items)
        {
            var memoryItem = CreateMemoryItem(content);
            var vector = CreateEmbeddingVector(vectorValues);
            await vectorStore.StoreAsync(id, memoryItem, vector, TestContext.Current.CancellationToken);
        }

        var count = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, count);

        // Search for items similar to "cats"
        var catsQuery = CreateEmbeddingVector(s_vector3D_100000);
        var catsResults = await vectorStore.SearchSimilarAsync(catsQuery, 10, 0.5f, TestContext.Current.CancellationToken);
        Assert.Equal(2, catsResults.Count()); // doc1 (perfect) and doc3 (good similarity)

        // Get specific item
        var doc2 = await vectorStore.GetAsync("doc2", TestContext.Current.CancellationToken);
        Assert.NotNull(doc2);
        Assert.Equal("Document about dogs", doc2.Content);

        // Remove item
        var removed = await vectorStore.RemoveAsync("doc2", TestContext.Current.CancellationToken);
        Assert.True(removed);

        var countAfterRemoval = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, countAfterRemoval);

        // Clear all
        await vectorStore.ClearAsync(TestContext.Current.CancellationToken);
        var finalCount = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, finalCount);
    }

    [Fact]
    public async Task ShouldMaintainConsistency_WhenIntegrationTestConcurrentOperations()
    {
        // Arrange
        using var vectorStore = new InMemoryVectorStore();
        var tasks = new List<Task>();

        // Act - Concurrent stores
        for (int i = 0; i < 20; i++)
        {
            int itemIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                var memoryItem = CreateMemoryItem($"Concurrent content {itemIndex}");
                var vector = CreateEmbeddingVector([itemIndex * 0.1f, itemIndex * 0.1f, 0.0f]);
                await vectorStore.StoreAsync($"concurrent-{itemIndex}", memoryItem, vector);
            }, TestContext.Current.CancellationToken));
        }

        // Concurrent searches
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                var queryVector = CreateEmbeddingVector(s_vector3D_050500);
                await vectorStore.SearchSimilarAsync(queryVector, 5, 0.0f);
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var finalCount = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(20, finalCount);

        // Final search should work
        var finalQuery = CreateEmbeddingVector(s_vector3D_101000);
        var finalResults = await vectorStore.SearchSimilarAsync(finalQuery, 5, 0.0f, TestContext.Current.CancellationToken);
        Assert.True(finalResults.Count() <= 5);
    }

    #endregion

    #region Helper Methods

    private static MemoryItem CreateMemoryItem(string content, DateTime? timestamp = null)
    {
        return MemoryItem.Create(
            content: content,
            embedding: s_vector3D_0102_03,
            importance: 1.0f);
    }

    private static EmbeddingVector CreateEmbeddingVector(float[] values)
    {
        return new EmbeddingVector(values);
    }

    #endregion
}
