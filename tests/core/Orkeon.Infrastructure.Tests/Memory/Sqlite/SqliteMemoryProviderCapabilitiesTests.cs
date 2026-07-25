using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory.Sqlite;

/// <summary>
/// Tests for the optional vector capabilities of <see cref="SqliteMemoryProvider"/>
/// (RAG-02/C4): <see cref="IScoredVectorSearch"/> (scores + keys + typed filter, caller's
/// <c>minScore</c> applied as given) and <see cref="IBatchUpsert"/> (transactional,
/// validate-first batch semantics).
/// </summary>
public sealed class SqliteMemoryProviderCapabilitiesTests : IDisposable
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] UnitX = [1f, 0f];
    private static readonly float[] UnitY = [0f, 1f];
    private static readonly float[] Diagonal = [0.7071f, 0.7071f];

    private readonly SqliteMemoryProvider _provider;

    public SqliteMemoryProviderCapabilitiesTests()
    {
        _provider = CreateProvider();
    }

    private static SqliteMemoryProvider CreateProvider(float minSimilarityScore = 0f) =>
        new(Options.Create(new SqliteMemoryOptions
            {
                ConnectionString = "Data Source=:memory:",
                MinSimilarityScore = minSimilarityScore
            }),
            new FakeFileSystemService());

    public void Dispose() => _provider.Dispose();

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
        IMemoryProvider provider = _provider;

        Assert.True(provider.TryGetCapability<IScoredVectorSearch>(out _));
        Assert.True(provider.TryGetCapability<IBatchUpsert>(out _));
        Assert.False(provider.TryGetCapability<IHybridSearchCapable>(out _));
    }

    // ── IScoredVectorSearch ───────────────────────────────────────────────

    [Fact]
    public async Task SearchSimilarWithScores_ReturnsScoresDescending_WithKeys()
    {
        await _provider.StoreAsync("k-x", CreateItem("x axis", UnitX), TestCt);
        await _provider.StoreAsync("k-diag", CreateItem("diagonal", Diagonal), TestCt);
        await _provider.StoreAsync("k-y", CreateItem("y axis", UnitY), TestCt);

        var results = await _provider.SearchSimilarWithScoresAsync(UnitX, topK: 10, minScore: 0.1f, cancellationToken: TestCt);

        Assert.Equal(2, results.Count);
        Assert.Equal("k-x", results[0].Key);
        Assert.Equal("k-diag", results[1].Key);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.All(results, r => Assert.True(r.Score > 0f));
    }

    [Fact]
    public async Task SearchSimilarWithScores_AppliesTypedFilter()
    {
        await _provider.StoreAsync("k-wiki", CreateItem("wiki item", UnitX, source: "wiki", tags: ["alpha"]), TestCt);
        await _provider.StoreAsync("k-web", CreateItem("web item", UnitX, source: "web", tags: ["beta"]), TestCt);

        var bySource = await _provider.SearchSimilarWithScoresAsync(
            UnitX, topK: 10, minScore: 0f, new MemoryFilter { Source = "wiki" }, TestCt);
        Assert.Single(bySource);
        Assert.Equal("k-wiki", bySource[0].Key);

        var byTag = await _provider.SearchSimilarWithScoresAsync(
            UnitX, topK: 10, minScore: 0f, new MemoryFilter { Tags = ["beta"] }, TestCt);
        Assert.Single(byTag);
        Assert.Equal("k-web", byTag[0].Key);
    }

    [Fact]
    public async Task SearchSimilarWithScores_AppliesCallerMinScore_NotProviderDefault()
    {
        // Provider configured with a high default threshold: the legacy path substitutes
        // it for minScore = 0, the capability path must NOT.
        using var provider = CreateProvider(minSimilarityScore: 0.9f);
        await provider.StoreAsync("k-diag", CreateItem("diagonal", Diagonal), TestCt);

        var legacy = await provider.SearchSimilarAsync(UnitX, topK: 10, minScore: 0f, cancellationToken: TestCt);
        Assert.Empty(legacy); // cosine(UnitX, Diagonal) ≈ 0.707 < 0.9 default

        var capability = await provider.SearchSimilarWithScoresAsync(UnitX, topK: 10, minScore: 0f, cancellationToken: TestCt);
        var single = Assert.Single(capability);
        Assert.Equal("k-diag", single.Key);
        Assert.InRange(single.Score, 0.7f, 0.72f);
    }

    // ── IBatchUpsert ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertBatch_StoresAllEntries_WithEmbeddings()
    {
        await _provider.UpsertBatchAsync(
        [
            new MemoryUpsertEntry("k-1", CreateItem("first"), UnitX),
            new MemoryUpsertEntry("k-2", CreateItem("second", UnitY)),
        ], TestCt);

        Assert.Equal(2, await _provider.CountAsync(TestCt));

        var first = await _provider.GetAsync("k-1", TestCt);
        Assert.NotNull(first);
        Assert.Equal("first", first.Content);
        Assert.NotNull(first.Embedding);
        Assert.Equal(UnitX, first.Embedding);
    }

    [Fact]
    public async Task UpsertBatch_ExistingKey_IsOverwritten()
    {
        await _provider.StoreAsync("k-1", CreateItem("old"), TestCt);

        await _provider.UpsertBatchAsync([new MemoryUpsertEntry("k-1", CreateItem("new"))], TestCt);

        var item = await _provider.GetAsync("k-1", TestCt);
        Assert.NotNull(item);
        Assert.Equal("new", item.Content);
        Assert.Equal(1, await _provider.CountAsync(TestCt));
    }

    [Fact]
    public async Task UpsertBatch_InvalidEntry_ThrowsAndWritesNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _provider.UpsertBatchAsync(
        [
            new MemoryUpsertEntry("k-valid", CreateItem("valid")),
            new MemoryUpsertEntry("   ", CreateItem("invalid key")),
        ], TestCt));

        Assert.Equal(0, await _provider.CountAsync(TestCt));
    }

    [Fact]
    public async Task UpsertBatch_ThenSearch_ScoresSurviveEndToEnd()
    {
        await _provider.UpsertBatchAsync(
        [
            new MemoryUpsertEntry("k-x", CreateItem("x axis"), UnitX),
            new MemoryUpsertEntry("k-y", CreateItem("y axis"), UnitY),
        ], TestCt);

        var results = await _provider.SearchSimilarWithScoresAsync(UnitX, topK: 10, minScore: 0.5f, cancellationToken: TestCt);

        var single = Assert.Single(results);
        Assert.Equal("k-x", single.Key);
        Assert.InRange(single.Score, 0.99f, 1.001f);
    }
}
