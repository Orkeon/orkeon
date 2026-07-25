using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// Tests for the optional vector capabilities of <see cref="InMemoryProvider"/> (RAG-02/C4):
/// <see cref="IScoredVectorSearch"/> (scores + keys + typed filter) and
/// <see cref="IBatchUpsert"/> (validate-first batch semantics).
/// </summary>
public class InMemoryProviderCapabilitiesTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] UnitX = [1f, 0f];
    private static readonly float[] UnitY = [0f, 1f];
    private static readonly float[] Diagonal = [0.7071f, 0.7071f];

    private static MemoryItem CreateItem(
        string content,
        float[]? embedding = null,
        string? source = null,
        IReadOnlyList<string>? tags = null)
        => MemoryItem.Create(content, embedding, source: source, tags: tags);

    // ── Capability discovery ──────────────────────────────────────────────

    [Fact]
    public void Provider_AdvertisesScoredVectorSearchAndBatchUpsert()
    {
        IMemoryProvider provider = new InMemoryProvider();

        Assert.True(provider.TryGetCapability<IScoredVectorSearch>(out _));
        Assert.True(provider.TryGetCapability<IBatchUpsert>(out _));
        Assert.False(provider.TryGetCapability<IHybridSearchCapable>(out _));
    }

    // ── IScoredVectorSearch ───────────────────────────────────────────────

    [Fact]
    public async Task SearchSimilarWithScores_ReturnsScoresDescending_WithKeys()
    {
        var provider = new InMemoryProvider();
        await provider.StoreAsync("k-x", CreateItem("x axis", UnitX), TestCt);
        await provider.StoreAsync("k-diag", CreateItem("diagonal", Diagonal), TestCt);
        await provider.StoreAsync("k-y", CreateItem("y axis", UnitY), TestCt);

        var results = await provider.SearchSimilarWithScoresAsync(UnitX, topK: 10, minScore: 0.1f, cancellationToken: TestCt);

        Assert.Equal(2, results.Count);
        Assert.Equal("k-x", results[0].Key);
        Assert.Equal("k-diag", results[1].Key);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.All(results, r => Assert.True(r.Score > 0f));
    }

    [Fact]
    public async Task SearchSimilarWithScores_HonorsMinScoreAndTopK()
    {
        var provider = new InMemoryProvider();
        await provider.StoreAsync("k-x", CreateItem("x axis", UnitX), TestCt);
        await provider.StoreAsync("k-diag", CreateItem("diagonal", Diagonal), TestCt);
        await provider.StoreAsync("k-y", CreateItem("y axis", UnitY), TestCt);

        var thresholded = await provider.SearchSimilarWithScoresAsync(UnitX, topK: 10, minScore: 0.9f, cancellationToken: TestCt);
        Assert.Single(thresholded);
        Assert.Equal("k-x", thresholded[0].Key);

        var capped = await provider.SearchSimilarWithScoresAsync(UnitX, topK: 1, minScore: 0f, cancellationToken: TestCt);
        Assert.Single(capped);
        Assert.Equal("k-x", capped[0].Key);
    }

    [Fact]
    public async Task SearchSimilarWithScores_AppliesTypedFilter()
    {
        var provider = new InMemoryProvider();
        await provider.StoreAsync("k-wiki", CreateItem("wiki item", UnitX, source: "wiki", tags: ["alpha"]), TestCt);
        await provider.StoreAsync("k-web", CreateItem("web item", UnitX, source: "web", tags: ["beta"]), TestCt);

        var bySource = await provider.SearchSimilarWithScoresAsync(
            UnitX, topK: 10, minScore: 0f, new MemoryFilter { Source = "wiki" }, TestCt);
        Assert.Single(bySource);
        Assert.Equal("k-wiki", bySource[0].Key);

        var byTag = await provider.SearchSimilarWithScoresAsync(
            UnitX, topK: 10, minScore: 0f, new MemoryFilter { Tags = ["beta"] }, TestCt);
        Assert.Single(byTag);
        Assert.Equal("k-web", byTag[0].Key);
    }

    [Fact]
    public async Task LegacySearchSimilar_NowAppliesDictionaryFilter()
    {
        var provider = new InMemoryProvider();
        await provider.StoreAsync("k-wiki", CreateItem("wiki item", UnitX, source: "wiki"), TestCt);
        await provider.StoreAsync("k-web", CreateItem("web item", UnitX, source: "web"), TestCt);

        var results = await provider.SearchSimilarAsync(
            UnitX, topK: 10, minScore: 0f,
            filter: new Dictionary<string, object> { ["source"] = "wiki" },
            cancellationToken: TestCt);

        var single = Assert.Single(results);
        Assert.Equal("wiki item", single.Item.Content);
    }

    // ── IBatchUpsert ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertBatch_StoresAllEntries_WithEmbeddings()
    {
        var provider = new InMemoryProvider();

        await provider.UpsertBatchAsync(
        [
            new MemoryUpsertEntry("k-1", CreateItem("first"), UnitX),
            new MemoryUpsertEntry("k-2", CreateItem("second", UnitY)),
        ], TestCt);

        var first = await provider.GetAsync("k-1", TestCt);
        Assert.NotNull(first);
        Assert.Equal("first", first.Content);
        Assert.NotNull(first.Embedding);
        Assert.Equal(UnitX, first.Embedding);

        var second = await provider.GetAsync("k-2", TestCt);
        Assert.NotNull(second);
        Assert.Equal(UnitY, second.Embedding);
    }

    [Fact]
    public async Task UpsertBatch_ExistingKey_IsOverwritten()
    {
        var provider = new InMemoryProvider();
        await provider.StoreAsync("k-1", CreateItem("old"), TestCt);

        await provider.UpsertBatchAsync([new MemoryUpsertEntry("k-1", CreateItem("new"))], TestCt);

        var item = await provider.GetAsync("k-1", TestCt);
        Assert.NotNull(item);
        Assert.Equal("new", item.Content);
        Assert.Equal(1, await provider.CountAsync(TestCt));
    }

    [Fact]
    public async Task UpsertBatch_InvalidEntry_ThrowsAndWritesNothing()
    {
        var provider = new InMemoryProvider();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.UpsertBatchAsync(
        [
            new MemoryUpsertEntry("k-valid", CreateItem("valid")),
            new MemoryUpsertEntry("   ", CreateItem("invalid key")),
        ], TestCt));

        Assert.Equal(0, await provider.CountAsync(TestCt));
    }

    [Fact]
    public async Task UpsertBatch_NullEntries_Throws()
    {
        var provider = new InMemoryProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.UpsertBatchAsync(null!, TestCt));
    }
}
