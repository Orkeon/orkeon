using Orkeon.Application.Constants.Rag;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Knowledge.Chunking;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using InfraKnowledgeService = Orkeon.Infrastructure.Knowledge.KnowledgeService;

namespace Orkeon.Infrastructure.Tests.Knowledge;

/// <summary>
/// Covers the semantic (vector) behavior of <see cref="InfraKnowledgeService"/>:
/// embedding at ingestion, cosine search through the memory provider, and the
/// relevance threshold (including the sane default).
/// </summary>
public sealed class KnowledgeServiceSemanticSearchTests
{
    private const string AlphaContent = "alpha content";
    private const string BetaContent = "beta content";
    private const string AlphaQuery = "find alpha";
    private const string BetaQuery = "find beta";

    private static readonly float[] s_alphaVector = [1f, 0f, 0f, 0f];
    private static readonly float[] s_betaVector = [0.6f, 0.8f, 0f, 0f];

    private static InfraKnowledgeService CreateService(IEmbeddingProvider embeddings, IMemoryProvider memory)
        => new(new RecursiveTextChunker(), new PassThroughFileSystemService(), embeddings, memory);

    private static MockEmbeddingProvider CreateAlphaBetaEmbeddings()
    {
        var embeddings = new MockEmbeddingProvider();
        embeddings.SetEmbeddingFunc(text => text switch
        {
            AlphaContent or AlphaQuery => s_alphaVector,
            BetaContent or BetaQuery => s_betaVector,
            _ => [0f, 0f, 0f, 1f]
        });
        return embeddings;
    }

    // ── Embedding at ingestion ──────────────────────────────────────────────

    [Fact]
    public async Task ShouldEmbedItemAndStoreInVectorIndex_WhenAddingKnowledge()
    {
        var embeddings = new MockEmbeddingProvider();
        embeddings.SetEmbeddingResult([0.1f, 0.2f, 0.3f]);
        var memory = new MockMemoryProvider();
        var service = CreateService(embeddings, memory);

        var id = await service.AddKnowledgeAsync("some direct knowledge", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, embeddings.GetEmbeddingCallCount);
        Assert.Equal("some direct knowledge", embeddings.LastGetEmbeddingText);
        Assert.Equal(1, memory.StoreWithEmbeddingCallCount);
        Assert.Equal(id, memory.LastStoreKey);
        Assert.NotNull(memory.LastStoreEmbedding);
        Assert.Equal([0.1f, 0.2f, 0.3f], memory.LastStoreEmbedding);

        var stats = await service.GetStatisticsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, stats.AverageEmbeddingDimension);
    }

    [Fact]
    public async Task ShouldBatchEmbedChunks_WhenLoadingSource()
    {
        var embeddings = new MockEmbeddingProvider();
        embeddings.SetEmbeddingResult([0.5f, 0.5f]);
        var memory = new MockMemoryProvider();
        var service = CreateService(embeddings, memory);

        var source = KnowledgeServiceTestsFixture.CreateMockSource(
            "embedded-source", "Content that gets embedded at ingestion time.");
        await service.AddSourceAsync(source, cancellationToken: TestContext.Current.CancellationToken);

        var stats = await service.GetStatisticsAsync(TestContext.Current.CancellationToken);
        Assert.True(stats.TotalItems > 0);
        Assert.Equal(1, embeddings.GetEmbeddingsCallCount);
        Assert.Equal(stats.TotalItems, memory.StoreWithEmbeddingCallCount);
        Assert.Equal(2, stats.AverageEmbeddingDimension);
    }

    [Fact]
    public async Task ShouldEmbedImportedItems_WhenImporting()
    {
        var embeddings = new MockEmbeddingProvider();
        embeddings.SetEmbeddingResult([0.4f, 0.6f]);
        var memory = new MockMemoryProvider();
        var service = CreateService(embeddings, memory);
        await service.AddKnowledgeAsync("exported item one", cancellationToken: TestContext.Current.CancellationToken);
        await service.AddKnowledgeAsync("exported item two", cancellationToken: TestContext.Current.CancellationToken);
        var path = Path.Combine(Path.GetTempPath(), $"knowledge_semantic_{Guid.NewGuid()}.json");

        try
        {
            await service.ExportAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            var importMemory = new MockMemoryProvider();
            var importEmbeddings = new MockEmbeddingProvider();
            importEmbeddings.SetEmbeddingResult([0.4f, 0.6f]);
            var importService = CreateService(importEmbeddings, importMemory);

            var count = await importService.ImportAsync(path, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, count);
            Assert.Equal(1, importEmbeddings.GetEmbeddingsCallCount);
            Assert.Equal(2, importMemory.StoreWithEmbeddingCallCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Cosine vector search ────────────────────────────────────────────────

    [Fact]
    public async Task ShouldDelegateToVectorSearch_WhenSearching()
    {
        var embeddings = new MockEmbeddingProvider();
        embeddings.SetEmbeddingResult([1f, 0f, 0f]);
        var memory = new MockMemoryProvider();
        var service = CreateService(embeddings, memory);

        var id = await service.AddKnowledgeAsync("indexed content", cancellationToken: TestContext.Current.CancellationToken);

        var matched = MemoryItem.Create(
            content: "indexed content",
            source: "direct",
            customProperties: new Dictionary<string, string> { ["knowledge_id"] = id });
        memory.SetSearchSimilarResult([new ScoredMemoryItem(matched, 0.87f)]);

        var results = await service.SearchAsync(
            "some query", topK: 3, minSimilarity: 0.42, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, memory.SearchSimilarCallCount);
        Assert.NotNull(memory.LastSearchSimilarQueryEmbedding);
        Assert.Equal([1f, 0f, 0f], memory.LastSearchSimilarQueryEmbedding);
        Assert.Equal(3, memory.LastSearchSimilarTopK);
        Assert.NotNull(memory.LastSearchSimilarMinScore);
        Assert.Equal(0.42f, memory.LastSearchSimilarMinScore.Value, 3);

        var item = Assert.Single(results);
        Assert.Equal(id, item.Id);
        Assert.NotNull(item.SimilarityScore);
        Assert.Equal(0.87, item.SimilarityScore.Value, 3);
    }

    [Fact]
    public async Task ShouldRankByCosineSimilarity_WhenSearching()
    {
        var service = CreateService(CreateAlphaBetaEmbeddings(), new InMemoryProvider());
        await service.AddKnowledgeAsync(AlphaContent, cancellationToken: TestContext.Current.CancellationToken);
        await service.AddKnowledgeAsync(BetaContent, cancellationToken: TestContext.Current.CancellationToken);

        var results = await service.SearchAsync(
            AlphaQuery, topK: 5, minSimilarity: 0.1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Equal(AlphaContent, results[0].Content);
        Assert.Equal(BetaContent, results[1].Content);
        Assert.NotNull(results[0].SimilarityScore);
        Assert.NotNull(results[1].SimilarityScore);
        Assert.Equal(1.0, results[0].SimilarityScore!.Value, 3);
        Assert.Equal(0.6, results[1].SimilarityScore!.Value, 3);
    }

    [Fact]
    public async Task ShouldRemoveFromVectorIndex_WhenDeletingKnowledge()
    {
        var service = CreateService(CreateAlphaBetaEmbeddings(), new InMemoryProvider());
        var id = await service.AddKnowledgeAsync(AlphaContent, cancellationToken: TestContext.Current.CancellationToken);

        var removed = await service.DeleteKnowledgeAsync(id, TestContext.Current.CancellationToken);
        var results = await service.SearchAsync(
            AlphaQuery, topK: 5, minSimilarity: 0.1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReembedItem_WhenUpdatingKnowledge()
    {
        var service = CreateService(CreateAlphaBetaEmbeddings(), new InMemoryProvider());
        var id = await service.AddKnowledgeAsync(AlphaContent, cancellationToken: TestContext.Current.CancellationToken);

        var updated = await service.UpdateKnowledgeAsync(id, BetaContent, cancellationToken: TestContext.Current.CancellationToken);
        var results = await service.SearchAsync(
            BetaQuery, topK: 5, minSimilarity: 0.9, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(updated);
        var item = Assert.Single(results);
        Assert.Equal(id, item.Id);
        Assert.Equal(BetaContent, item.Content);
        Assert.Equal(1.0, item.SimilarityScore!.Value, 3);
    }

    // ── Relevance threshold ─────────────────────────────────────────────────

    [Fact]
    public async Task ShouldFilterResultsBelowThreshold_WhenSearchingWithExplicitMinSimilarity()
    {
        var service = CreateService(CreateAlphaBetaEmbeddings(), new InMemoryProvider());
        await service.AddKnowledgeAsync(AlphaContent, cancellationToken: TestContext.Current.CancellationToken);
        await service.AddKnowledgeAsync(BetaContent, cancellationToken: TestContext.Current.CancellationToken);

        var results = await service.SearchAsync(
            AlphaQuery, topK: 5, minSimilarity: 0.8, cancellationToken: TestContext.Current.CancellationToken);

        var item = Assert.Single(results);
        Assert.Equal(AlphaContent, item.Content);
    }

    [Fact]
    public async Task ShouldIncludeMidRelevanceResults_WhenSearchingWithDefaultThreshold()
    {
        // The old 0.7 default silently dropped the 0.6-scored item below.
        var service = CreateService(CreateAlphaBetaEmbeddings(), new InMemoryProvider());
        await service.AddKnowledgeAsync(AlphaContent, cancellationToken: TestContext.Current.CancellationToken);
        await service.AddKnowledgeAsync(BetaContent, cancellationToken: TestContext.Current.CancellationToken);

        var results = await service.SearchAsync(AlphaQuery, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, item => item.Content == BetaContent);
    }

    [Fact]
    public void ShouldExposeSaneDefaultMinRelevanceScore()
    {
        Assert.Equal(0.3f, RagDefaults.DefaultMinRelevanceScore);
    }
}
