using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory.Sqlite;

/// <summary>
/// Tests for <see cref="SqliteMemoryProvider"/> (R3.2): real SQLite-backed
/// <see cref="IMemoryProvider"/> with content, embedding and metadata persistence,
/// LIKE-based full-text search and cosine-similarity vector search.
/// </summary>
public sealed class SqliteMemoryProviderTests : IDisposable
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly string[] AlphaBetaTags = ["alpha", "beta"];
    private static readonly float[] Embedding123 = [0.1f, 0.2f, 0.3f];
    private static readonly float[] EmbeddingHalfHalf = [0.5f, 0.5f];
    private static readonly float[] EmbeddingOneZero = [1f, 0f];

    private readonly SqliteMemoryProvider _provider;

    public SqliteMemoryProviderTests()
    {
        _provider = CreateInMemoryDbProvider();
    }

    private static SqliteMemoryProvider CreateInMemoryDbProvider() =>
        new(Options.Create(new SqliteMemoryOptions { ConnectionString = "Data Source=:memory:" }),
            new FakeFileSystemService());

    public void Dispose() => _provider.Dispose();

    // ── Construction ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_InvalidTableName_Throws()
    {
        var options = Options.Create(new SqliteMemoryOptions
        {
            ConnectionString = "Data Source=:memory:",
            TableName = "items; DROP TABLE users;--"
        });

        Assert.Throws<ArgumentException>(() => new SqliteMemoryProvider(options, new FakeFileSystemService()));
    }

    [Fact]
    public void Name_IsSqlite()
    {
        Assert.Equal("SQLite", _provider.Name);
    }

    // ── Store / Get round-trip ────────────────────────────────────────────

    [Fact]
    public async Task StoreAndGet_RoundTrips_ContentEmbeddingAndMetadata()
    {
        // Arrange
        var item = MemoryItem.Create(
            "the quick brown fox",
            embedding: [0.1f, 0.2f, 0.3f],
            importance: 0.8f,
            source: "unit-test",
            tags: ["alpha", "beta"],
            customProperties: new Dictionary<string, string> { ["origin"] = "r3.2" });

        // Act
        await _provider.StoreAsync("key-1", item, TestCt);
        var retrieved = await _provider.GetAsync("key-1", TestCt);

        // Assert — identifier and metadata are preserved (rehydrated, not regenerated)
        Assert.NotNull(retrieved);
        Assert.Equal(item.Id, retrieved.Id);
        Assert.Equal("the quick brown fox", retrieved.Content);
        Assert.Equal(0.8f, retrieved.Importance);
        Assert.Equal("unit-test", retrieved.Source);
        Assert.Equal(AlphaBetaTags, retrieved.Tags);
        Assert.NotNull(retrieved.Embedding);
        Assert.Equal(Embedding123, retrieved.Embedding);
        Assert.NotNull(retrieved.Metadata.CustomProperties);
        Assert.Equal("r3.2", retrieved.Metadata.CustomProperties["origin"]);
    }

    [Fact]
    public async Task Get_UnknownKey_ReturnsNull()
    {
        var retrieved = await _provider.GetAsync("missing-key", TestCt);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task Store_SameKeyTwice_Upserts()
    {
        await _provider.StoreAsync("key-1", MemoryItem.Create("first version"), TestCt);
        await _provider.StoreAsync("key-1", MemoryItem.Create("second version"), TestCt);

        var retrieved = await _provider.GetAsync("key-1", TestCt);

        Assert.NotNull(retrieved);
        Assert.Equal("second version", retrieved.Content);
        Assert.Equal(1, await _provider.CountAsync(TestCt));
    }

    // ── Update ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingKey_ReturnsTrue_AndPersistsNewContent()
    {
        await _provider.StoreAsync("key-1", MemoryItem.Create("original"), TestCt);

        var updated = await _provider.UpdateAsync("key-1", MemoryItem.Create("modified"), TestCt);
        var retrieved = await _provider.GetAsync("key-1", TestCt);

        Assert.True(updated);
        Assert.NotNull(retrieved);
        Assert.Equal("modified", retrieved.Content);
    }

    [Fact]
    public async Task Update_MissingKey_ReturnsFalse_AndDoesNotInsert()
    {
        var updated = await _provider.UpdateAsync("ghost", MemoryItem.Create("never stored"), TestCt);

        Assert.False(updated);
        Assert.Equal(0, await _provider.CountAsync(TestCt));
    }

    // ── Delete / Clear / Count / ListKeys ─────────────────────────────────

    [Fact]
    public async Task Delete_RemovesItem_SecondDeleteReturnsFalse()
    {
        await _provider.StoreAsync("key-1", MemoryItem.Create("to delete"), TestCt);

        var first = await _provider.DeleteAsync("key-1", TestCt);
        var second = await _provider.DeleteAsync("key-1", TestCt);

        Assert.True(first);
        Assert.False(second);
        Assert.Null(await _provider.GetAsync("key-1", TestCt));
    }

    [Fact]
    public async Task Clear_RemovesAllItems()
    {
        await _provider.StoreAsync("key-1", MemoryItem.Create("one"), TestCt);
        await _provider.StoreAsync("key-2", MemoryItem.Create("two"), TestCt);

        await _provider.ClearAsync(TestCt);

        Assert.Equal(0, await _provider.CountAsync(TestCt));
    }

    [Fact]
    public async Task ListKeys_Paginates_InKeyOrder()
    {
        await _provider.StoreAsync("key-a", MemoryItem.Create("a"), TestCt);
        await _provider.StoreAsync("key-b", MemoryItem.Create("b"), TestCt);
        await _provider.StoreAsync("key-c", MemoryItem.Create("c"), TestCt);

        var page = await _provider.ListKeysAsync(skip: 1, take: 1, TestCt);

        Assert.Single(page);
        Assert.Equal("key-b", page[0]);
    }

    // ── Full-text search ──────────────────────────────────────────────────

    [Fact]
    public async Task Search_NullQuery_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _provider.SearchAsync(null!, cancellationToken: TestCt));
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsAllItemsUpToLimit()
    {
        await _provider.StoreAsync("key-1", MemoryItem.Create("one"), TestCt);
        await _provider.StoreAsync("key-2", MemoryItem.Create("two"), TestCt);
        await _provider.StoreAsync("key-3", MemoryItem.Create("three"), TestCt);

        var results = await _provider.SearchAsync("", limit: 2, TestCt);

        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task Search_MatchesContent_CaseInsensitively()
    {
        await _provider.StoreAsync("key-1", MemoryItem.Create("The Quick Brown Fox"), TestCt);
        await _provider.StoreAsync("key-2", MemoryItem.Create("something else entirely"), TestCt);

        var results = (await _provider.SearchAsync("quick brown", cancellationToken: TestCt)).ToList();

        Assert.Single(results);
        Assert.Equal("The Quick Brown Fox", results[0].Content);
    }

    [Fact]
    public async Task Search_EscapesLikeWildcards_MatchingLiterally()
    {
        await _provider.StoreAsync("key-1", MemoryItem.Create("progress at 100% complete"), TestCt);
        await _provider.StoreAsync("key-2", MemoryItem.Create("progress at 100 complete"), TestCt);

        // '%' must be matched literally, not as a LIKE wildcard.
        var results = (await _provider.SearchAsync("100%", cancellationToken: TestCt)).ToList();

        Assert.Single(results);
        Assert.Equal("progress at 100% complete", results[0].Content);
    }

    // ── Vector search (cosine similarity) ─────────────────────────────────

    [Fact]
    public async Task SearchSimilar_OrdersByCosineSimilarity_BestFirst()
    {
        await _provider.StoreAsync("identical", MemoryItem.Create("identical", embedding: [1f, 0f]), TestCt);
        await _provider.StoreAsync("close", MemoryItem.Create("close", embedding: [0.9f, 0.1f]), TestCt);
        await _provider.StoreAsync("orthogonal", MemoryItem.Create("orthogonal", embedding: [0f, 1f]), TestCt);

        var results = await _provider.SearchSimilarAsync([1f, 0f], topK: 10, minScore: 0f, cancellationToken: TestCt);

        Assert.Equal(3, results.Count);
        Assert.Equal("identical", results[0].Item.Content);
        Assert.Equal("close", results[1].Item.Content);
        Assert.Equal("orthogonal", results[2].Item.Content);
        Assert.Equal(1f, results[0].Score, 0.001f);
    }

    [Fact]
    public async Task SearchSimilar_AppliesMinScoreThreshold()
    {
        await _provider.StoreAsync("identical", MemoryItem.Create("identical", embedding: [1f, 0f]), TestCt);
        await _provider.StoreAsync("orthogonal", MemoryItem.Create("orthogonal", embedding: [0f, 1f]), TestCt);

        var results = await _provider.SearchSimilarAsync([1f, 0f], topK: 10, minScore: 0.5f, cancellationToken: TestCt);

        Assert.Single(results);
        Assert.Equal("identical", results[0].Item.Content);
    }

    [Fact]
    public async Task SearchSimilar_SkipsItemsWithoutEmbedding_AndDimensionMismatches()
    {
        await _provider.StoreAsync("no-embedding", MemoryItem.Create("no embedding"), TestCt);
        await _provider.StoreAsync("wrong-dim", MemoryItem.Create("wrong dim", embedding: [1f, 0f, 0f]), TestCt);
        await _provider.StoreAsync("match", MemoryItem.Create("match", embedding: [1f, 0f]), TestCt);

        var results = await _provider.SearchSimilarAsync([1f, 0f], topK: 10, minScore: 0f, cancellationToken: TestCt);

        Assert.Single(results);
        Assert.Equal("match", results[0].Item.Content);
    }

    [Fact]
    public async Task SearchSimilar_FiltersBySource()
    {
        await _provider.StoreAsync("a", MemoryItem.Create("from alpha", embedding: [1f, 0f], source: "alpha"), TestCt);
        await _provider.StoreAsync("b", MemoryItem.Create("from beta", embedding: [1f, 0f], source: "beta"), TestCt);

        var results = await _provider.SearchSimilarAsync(
            [1f, 0f], topK: 10, minScore: 0f,
            filter: new Dictionary<string, object> { ["source"] = "alpha" },
            cancellationToken: TestCt);

        Assert.Single(results);
        Assert.Equal("from alpha", results[0].Item.Content);
    }

    // ── Interface dispatch (re-implementation of the IMemoryProvider defaults) ──

    [Fact]
    public async Task SearchSimilar_ThroughInterfaceDispatch_UsesRealImplementation()
    {
        // The IMemoryProvider default bodies are a no-op (empty results). The provider
        // re-lists the interface so calls through IMemoryProvider reach the real search.
        IMemoryProvider viaInterface = _provider;

        await viaInterface.StoreWithEmbeddingAsync("key-1", MemoryItem.Create("vectorized"), [1f, 0f], TestCt);
        var results = await viaInterface.SearchSimilarAsync([1f, 0f], topK: 5, cancellationToken: TestCt);

        Assert.Single(results);
        Assert.Equal("vectorized", results[0].Item.Content);
    }

    [Fact]
    public async Task StoreWithEmbedding_PersistsEmbedding()
    {
        await _provider.StoreWithEmbeddingAsync("key-1", MemoryItem.Create("payload"), [0.5f, 0.5f], TestCt);

        var retrieved = await _provider.GetAsync("key-1", TestCt);

        Assert.NotNull(retrieved);
        Assert.Equal(EmbeddingHalfHalf, retrieved.Embedding);
    }

    // ── Persistence across provider instances (real file database) ───────────

    [Fact]
    public async Task Data_PersistsAcrossProviderInstances_WithFileDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"orkeon-sqlite-mem-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            var item = MemoryItem.Create("survives restarts", embedding: [1f, 0f], source: "persist-test");

            using (var first = new SqliteMemoryProvider(
                Options.Create(new SqliteMemoryOptions { ConnectionString = connectionString }),
                new FakeFileSystemService()))
            {
                await first.StoreAsync("durable-key", item, TestCt);
            }

            using var second = new SqliteMemoryProvider(
                Options.Create(new SqliteMemoryOptions { ConnectionString = connectionString }),
                new FakeFileSystemService());

            var retrieved = await second.GetAsync("durable-key", TestCt);

            Assert.NotNull(retrieved);
            Assert.Equal("survives restarts", retrieved.Content);
            Assert.Equal(item.Id, retrieved.Id);
            Assert.Equal(EmbeddingOneZero, retrieved.Embedding);
        }
        finally
        {
            // Release pooled connections before removing the database file.
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}
