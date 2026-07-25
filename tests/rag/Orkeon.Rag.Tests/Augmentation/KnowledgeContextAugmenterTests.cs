using Orkeon.Domain.Knowledge;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Augmentation;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Augmentation;

/// <summary>
/// Unit tests for <see cref="KnowledgeContextAugmenter"/> (RAG-03/C4): block
/// formatting, global citation numbering, MinScore filtering, per-attachment
/// token budget (chars ×4 heuristic), multi-collection queries, and the
/// no-result → null contract.
/// </summary>
public class KnowledgeContextAugmenterTests
{
    private static ScoredChunk MakeChunk(
        string id, string sourceId, string content, double score, string documentId = "doc-1") =>
        new()
        {
            Chunk = new Chunk
            {
                Id = id,
                DocumentId = documentId,
                SourceId = sourceId,
                Content = content,
                StartOffset = 0,
                EndOffset = content.Length,
            },
            Score = score,
        };

    private static KnowledgeContextAugmenter CreateAugmenter(StubDocumentStore store) =>
        new(store, new FakeEmbeddingProvider());

    [Fact]
    public async Task BuildContextAsync_WithResults_ProducesHeaderInstructionAndNumberedExcerpts()
    {
        var store = new StubDocumentStore();
        store.ResultsByCollection["produits"] =
        [
            MakeChunk("c1", "faq.md", "Refunds are possible within 30 days.", 0.91),
            MakeChunk("c2", "catalogue.pdf", "The Pro plan includes 5 seats.", 0.72),
        ];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("produits")], "refund policy", TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.StartsWith(KnowledgeContextAugmenter.BlockHeader, block.Text, StringComparison.Ordinal);
        Assert.Contains(KnowledgeContextAugmenter.GroundingInstruction, block.Text, StringComparison.Ordinal);
        Assert.Contains("[1] (collection: produits, source: faq.md, score: 0.91)", block.Text, StringComparison.Ordinal);
        Assert.Contains("Refunds are possible within 30 days.", block.Text, StringComparison.Ordinal);
        Assert.Contains("[2] (collection: produits, source: catalogue.pdf, score: 0.72)", block.Text, StringComparison.Ordinal);

        Assert.Equal(2, block.Citations.Count);
        Assert.Equal(1, block.Citations[0].Marker);
        Assert.Equal("c1", block.Citations[0].ChunkId);
        Assert.Equal("faq.md", block.Citations[0].SourceId);
        Assert.Equal(0.91, block.Citations[0].Score);
        Assert.Equal("Refunds are possible within 30 days.", block.Citations[0].Snippet);
        Assert.Equal(2, block.Citations[1].Marker);
    }

    [Fact]
    public async Task BuildContextAsync_MultipleCollections_NumbersCitationsGlobally()
    {
        var store = new StubDocumentStore();
        store.ResultsByCollection["produits"] =
            [MakeChunk("p1", "faq.md", "Product excerpt.", 0.9)];
        store.ResultsByCollection["procedures"] =
            [MakeChunk("q1", "proc.md", "Procedure excerpt.", 0.8)];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("produits"), KnowledgeAttachment.Create("procedures")],
            "how do I return a product?",
            TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.Equal(2, store.SearchCalls.Count);
        Assert.Equal("produits", store.SearchCalls[0].Collection);
        Assert.Equal("procedures", store.SearchCalls[1].Collection);
        Assert.Contains("[1] (collection: produits", block.Text, StringComparison.Ordinal);
        Assert.Contains("[2] (collection: procedures", block.Text, StringComparison.Ordinal);
        Assert.Equal([1, 2], block.Citations.Select(c => c.Marker));
        Assert.Equal(["p1", "q1"], block.Citations.Select(c => c.ChunkId));
    }

    [Fact]
    public async Task BuildContextAsync_PassesAttachmentTopKAndQueryEmbeddingToTheStore()
    {
        var store = new StubDocumentStore();
        store.ResultsByCollection["docs"] = [MakeChunk("c1", "a.md", "text", 0.5)];
        var embeddings = new FakeEmbeddingProvider { Dimensions = 3 };
        var augmenter = new KnowledgeContextAugmenter(store, embeddings);

        await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs", topK: 7)], "the question", TestContext.Current.CancellationToken);

        var (_, query) = Assert.Single(store.SearchCalls);
        Assert.Equal(7, query.TopK);
        Assert.Equal("the question", query.Text);
        Assert.NotNull(query.Embedding);
        Assert.Equal(3, query.Embedding.Value.Length);
        Assert.Equal("the question", Assert.Single(embeddings.UnaryCalls));
    }

    [Fact]
    public async Task BuildContextAsync_MinScore_FiltersLowScoredChunks()
    {
        var store = new StubDocumentStore();
        store.ResultsByCollection["docs"] =
        [
            MakeChunk("hi", "high.md", "High-relevance excerpt.", 0.85),
            MakeChunk("lo", "low.md", "Low-relevance excerpt.", 0.30),
        ];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs", minScore: 0.5)], "query", TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.Contains("High-relevance excerpt.", block.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Low-relevance excerpt.", block.Text, StringComparison.Ordinal);
        Assert.Equal("hi", Assert.Single(block.Citations).ChunkId);
    }

    [Fact]
    public async Task BuildContextAsync_TopK_KeepsBestChunksOnly()
    {
        var store = new StubDocumentStore();
        store.ResultsByCollection["docs"] =
        [
            MakeChunk("worst", "w.md", "Worst.", 0.10),
            MakeChunk("best", "b.md", "Best.", 0.99),
            MakeChunk("middle", "m.md", "Middle.", 0.50),
        ];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs", topK: 2)], "query", TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.Equal(["best", "middle"], block.Citations.Select(c => c.ChunkId));
    }

    [Fact]
    public async Task BuildContextAsync_MaxContextTokens_TruncatesExcerptAtCharsTimesFour()
    {
        // Heuristic under test: budget chars = MaxContextTokens × 4.
        var store = new StubDocumentStore();
        var longContent = new string('a', 200);
        store.ResultsByCollection["docs"] =
        [
            MakeChunk("c1", "a.md", longContent, 0.9),
            MakeChunk("c2", "b.md", "Never reached.", 0.8),
        ];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs", maxContextTokens: 10)], "query", TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        var expectedExcerpt = new string('a', 40) + KnowledgeContextAugmenter.TruncationSuffix;
        Assert.Contains(expectedExcerpt, block.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 41), block.Text, StringComparison.Ordinal);
        // Budget exhausted by the first excerpt — the second chunk is dropped.
        Assert.DoesNotContain("Never reached.", block.Text, StringComparison.Ordinal);
        Assert.Equal("c1", Assert.Single(block.Citations).ChunkId);
    }

    [Fact]
    public async Task BuildContextAsync_BudgetIsPerAttachment_SecondCollectionStillInjected()
    {
        var store = new StubDocumentStore();
        store.ResultsByCollection["big"] = [MakeChunk("c1", "big.md", new string('x', 500), 0.9)];
        store.ResultsByCollection["small"] = [MakeChunk("c2", "small.md", "Small excerpt.", 0.9)];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [
                KnowledgeAttachment.Create("big", maxContextTokens: 10),
                KnowledgeAttachment.Create("small"),
            ],
            "query", TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.Contains("Small excerpt.", block.Text, StringComparison.Ordinal);
        Assert.Equal(2, block.Citations.Count);
    }

    [Fact]
    public async Task BuildContextAsync_NoResultAnywhere_ReturnsNull()
    {
        var store = new StubDocumentStore(); // no scripted result: every search is empty
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("produits"), KnowledgeAttachment.Create("procedures")],
            "query", TestContext.Current.CancellationToken);

        Assert.Null(block);
        Assert.Equal(2, store.SearchCalls.Count);
    }

    [Fact]
    public async Task BuildContextAsync_AllChunksBelowMinScore_ReturnsNull()
    {
        var store = new StubDocumentStore();
        store.ResultsByCollection["docs"] = [MakeChunk("c1", "a.md", "text", 0.10)];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs", minScore: 0.9)], "query", TestContext.Current.CancellationToken);

        Assert.Null(block);
    }

    [Fact]
    public async Task BuildContextAsync_EmptyAttachments_ReturnsNullWithoutEmbeddingOrSearch()
    {
        var store = new StubDocumentStore();
        var embeddings = new FakeEmbeddingProvider();
        var augmenter = new KnowledgeContextAugmenter(store, embeddings);

        var block = await augmenter.BuildContextAsync([], "query", TestContext.Current.CancellationToken);

        Assert.Null(block);
        Assert.Empty(embeddings.UnaryCalls);
        Assert.Empty(store.SearchCalls);
    }

    [Fact]
    public async Task BuildContextAsync_BlankTaskInput_ReturnsNullWithoutSearch()
    {
        var store = new StubDocumentStore();
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("docs")], "   ", TestContext.Current.CancellationToken);

        Assert.Null(block);
        Assert.Empty(store.SearchCalls);
    }

    [Fact]
    public async Task BuildContextAsync_DuplicateChunkAcrossAttachments_IsCitedOnce()
    {
        var store = new StubDocumentStore();
        var shared = MakeChunk("same", "shared.md", "Shared excerpt.", 0.9);
        store.ResultsByCollection["a"] = [shared];
        store.ResultsByCollection["b"] = [shared];
        var augmenter = CreateAugmenter(store);

        var block = await augmenter.BuildContextAsync(
            [KnowledgeAttachment.Create("a"), KnowledgeAttachment.Create("b")], "query", TestContext.Current.CancellationToken);

        Assert.NotNull(block);
        Assert.Equal("same", Assert.Single(block.Citations).ChunkId);
    }

    [Fact]
    public async Task BuildContextAsync_NullAttachments_Throws()
    {
        var augmenter = CreateAugmenter(new StubDocumentStore());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => augmenter.BuildContextAsync(null!, "query", TestContext.Current.CancellationToken));
    }
}
