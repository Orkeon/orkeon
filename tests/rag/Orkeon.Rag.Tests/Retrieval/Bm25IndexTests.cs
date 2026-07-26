using Orkeon.Rag.Retrieval;
using static Orkeon.Rag.Tests.Retrieval.RetrievalTestData;

namespace Orkeon.Rag.Tests.Retrieval;

/// <summary>
/// Tests of the in-process BM25 index (RAG-04/C2): term-frequency monotonicity, IDF
/// penalty of corpus-frequent terms, robust tokenization, and index maintenance
/// (upsert-by-id, purge-by-source).
/// </summary>
public class Bm25IndexTests
{
    [Fact]
    public void Search_ScoreGrowsWithTermFrequency_AtEqualDocumentLength()
    {
        var index = new Bm25Index();
        index.UpsertRange(
        [
            MakeChunk("a", "cat dog dog"),
            MakeChunk("b", "cat cat dog"),
        ]);

        var results = index.Search("cat", topK: 10);

        Assert.Equal(2, results.Count);
        Assert.Equal("b", results[0].Chunk.Id); // tf(cat) = 2 beats tf(cat) = 1
        Assert.Equal("a", results[1].Chunk.Id);
        Assert.True(results[0].Score > results[1].Score);
        Assert.All(results, r => Assert.Equal(Bm25Index.Bm25ScoreOrigin, r.ScoreOrigin));
    }

    [Fact]
    public void Search_IdfPenalizesCorpusFrequentTerms()
    {
        // "common" appears in every document, "rare" in a single one; equal lengths.
        var index = new Bm25Index();
        index.UpsertRange(
        [
            MakeChunk("d1", "common rare"),
            MakeChunk("d2", "common alpha"),
            MakeChunk("d3", "common beta"),
            MakeChunk("d4", "common gamma"),
        ]);

        var rareTop = index.Search("rare", topK: 1)[0];
        var commonTop = index.Search("common", topK: 1)[0];

        Assert.Equal("d1", rareTop.Chunk.Id);
        Assert.True(rareTop.Score > commonTop.Score,
            $"idf must reward the rare term (rare: {rareTop.Score}, common: {commonTop.Score})");
    }

    [Fact]
    public void Search_EmptyOrNonLexicalQuery_ReturnsEmpty()
    {
        var index = new Bm25Index();
        index.UpsertRange([MakeChunk("a", "some content")]);

        Assert.Empty(index.Search("", topK: 5));
        Assert.Empty(index.Search("   ", topK: 5));
        Assert.Empty(index.Search("!!! ??? ---", topK: 5));
    }

    [Fact]
    public void Search_UnknownTerm_ReturnsEmpty()
    {
        var index = new Bm25Index();
        index.UpsertRange([MakeChunk("a", "some content")]);

        Assert.Empty(index.Search("zebra", topK: 5));
    }

    [Fact]
    public void Search_IsCaseInsensitive_InvariantLowercase()
    {
        var index = new Bm25Index();
        index.UpsertRange([MakeChunk("a", "The CAT sat on the mat.")]);

        var single = Assert.Single(index.Search("cat", topK: 5));
        Assert.Equal("a", single.Chunk.Id);
        Assert.Single(index.Search("MAT", topK: 5));
    }

    [Fact]
    public void Search_TruncatesToTopK_BestFirst()
    {
        var index = new Bm25Index();
        index.UpsertRange(
        [
            MakeChunk("low", "cat dog bird fish"),
            MakeChunk("high", "cat cat cat cat"),
            MakeChunk("mid", "cat cat bird fish"),
        ]);

        var results = index.Search("cat", topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("high", results[0].Chunk.Id);
        Assert.Equal("mid", results[1].Chunk.Id);
    }

    [Fact]
    public void UpsertRange_SameChunkId_ReplacesTheIndexedContent()
    {
        var index = new Bm25Index();
        index.UpsertRange([MakeChunk("a", "alpha")]);
        index.UpsertRange([MakeChunk("a", "beta")]);

        Assert.Equal(1, index.Count);
        Assert.Empty(index.Search("alpha", topK: 5));
        Assert.Single(index.Search("beta", topK: 5));
    }

    [Fact]
    public void RemoveSource_PurgesOnlyThatSourcesChunks()
    {
        var index = new Bm25Index();
        index.UpsertRange(
        [
            MakeChunk("a1", "quantum entanglement", sourceId: "physics.md"),
            MakeChunk("a2", "quantum computing", sourceId: "physics.md"),
            MakeChunk("b1", "quantum of solace", sourceId: "movies.md"),
        ]);

        index.RemoveSource("physics.md");

        Assert.Equal(1, index.Count);
        var single = Assert.Single(index.Search("quantum", topK: 5));
        Assert.Equal("b1", single.Chunk.Id);
        Assert.Empty(index.Search("entanglement", topK: 5));
    }

    [Fact]
    public void Search_NonPositiveTopK_Throws()
    {
        var index = new Bm25Index();
        Assert.Throws<ArgumentOutOfRangeException>(() => index.Search("x", topK: 0));
    }

    [Fact]
    public void Constructor_RejectsInvalidParameters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25Index(k1: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25Index(b: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25Index(b: 1.1));
    }

    [Fact]
    public void Tokenize_SplitsOnNonLetterOrDigit_AndLowercases()
    {
        var tokens = Bm25Index.Tokenize("Été 2026: RAG-04/C2, précision!");

        Assert.Equal(["été", "2026", "rag", "04", "c2", "précision"], tokens);
    }
}
