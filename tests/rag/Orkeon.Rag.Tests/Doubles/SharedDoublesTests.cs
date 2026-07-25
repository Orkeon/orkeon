using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Behavioral tests for the shared hand-written doubles introduced with the RAG
/// subsystem (<see cref="StubReranker"/>, <see cref="FakeDocumentStore"/>), so
/// downstream test suites can rely on them.
/// </summary>
public class SharedDoublesTests
{
    private static Chunk MakeChunk(string id, string content, string sourceId = "s1") => new()
    {
        Id = id,
        DocumentId = "d1",
        SourceId = sourceId,
        Content = content,
    };

    private static EmbeddedChunk Embed(Chunk chunk, params float[] vector) => new()
    {
        Chunk = chunk,
        Embedding = [.. vector],
    };

    private static ScoredChunk Score(string id, double score) => new()
    {
        Chunk = MakeChunk(id, $"content {id}"),
        Score = score,
    };

    [Fact]
    public async Task StubReranker_Default_KeepsOrder_AndTruncatesToTopN()
    {
        var reranker = new StubReranker();
        var candidates = new[] { Score("a", 0.9), Score("b", 0.8), Score("c", 0.7) };

        var result = await reranker.RerankAsync("q", candidates, topN: 2, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], result.Select(s => s.Chunk.Id));
        Assert.Equal(["q"], reranker.Queries);
        Assert.Equal(2, reranker.LastTopN);
    }

    [Fact]
    public async Task StubReranker_ReverseOrder_ReversesCandidates()
    {
        var reranker = new StubReranker { ReverseOrder = true };
        var candidates = new[] { Score("a", 0.9), Score("b", 0.8), Score("c", 0.7) };

        var result = await reranker.RerankAsync("q", candidates, topN: 3, TestContext.Current.CancellationToken);

        Assert.Equal(["c", "b", "a"], result.Select(s => s.Chunk.Id));
    }

    [Fact]
    public async Task FakeDocumentStore_UpsertThenVectorSearch_RanksByCosineSimilarity()
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync(
            "docs",
            [
                Embed(MakeChunk("aligned", "aligned"), 1f, 0f),
                Embed(MakeChunk("orthogonal", "orthogonal"), 0f, 1f),
            ],
            TestContext.Current.CancellationToken);

        var results = await store.SearchAsync(
            "docs",
            new RetrievalQuery { Text = "q", Embedding = ImmutableArray.Create(1f, 0f), TopK = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Equal("aligned", results[0].Chunk.Id);
        Assert.Equal(1.0, results[0].Score, precision: 6);
        Assert.Equal(0.0, results[1].Score, precision: 6);
        Assert.Equal("vector", results[0].ScoreOrigin);
    }

    [Fact]
    public async Task FakeDocumentStore_TextSearch_HonorsFiltersAndTopK()
    {
        var store = new FakeDocumentStore();
        var tagged = MakeChunk("tagged", "the answer is 42") with
        {
            Metadata = ImmutableDictionary<string, string>.Empty.Add("lang", "fr"),
        };
        await store.UpsertAsync(
            "docs",
            [Embed(tagged, 1f), Embed(MakeChunk("plain", "the answer is 42"), 1f)],
            TestContext.Current.CancellationToken);

        var results = await store.SearchAsync(
            "docs",
            new RetrievalQuery
            {
                Text = "answer",
                TopK = 5,
                Filters = ImmutableDictionary<string, string>.Empty.Add("lang", "fr"),
            },
            TestContext.Current.CancellationToken);

        var only = Assert.Single(results);
        Assert.Equal("tagged", only.Chunk.Id);
        Assert.Equal(1.0, only.Score);
    }

    [Fact]
    public async Task FakeDocumentStore_Upsert_IsIdempotentByChunkId()
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync("docs", [Embed(MakeChunk("c1", "v1"), 1f)], TestContext.Current.CancellationToken);
        await store.UpsertAsync("docs", [Embed(MakeChunk("c1", "v2"), 1f)], TestContext.Current.CancellationToken);

        Assert.Equal(1, store.Count("docs"));
        Assert.Equal("v2", store.GetCollection("docs")[0].Chunk.Content);
    }

    [Fact]
    public async Task FakeDocumentStore_DeleteBySource_RemovesOnlyThatSource()
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync(
            "docs",
            [
                Embed(MakeChunk("c1", "x", sourceId: "keep"), 1f),
                Embed(MakeChunk("c2", "x", sourceId: "drop"), 1f),
                Embed(MakeChunk("c3", "x", sourceId: "drop"), 1f),
            ],
            TestContext.Current.CancellationToken);

        await store.DeleteBySourceAsync("docs", "drop", TestContext.Current.CancellationToken);

        var remaining = Assert.Single(store.GetCollection("docs"));
        Assert.Equal("c1", remaining.Chunk.Id);
    }

    [Fact]
    public async Task FakeDocumentStore_SearchUnknownCollection_ReturnsEmpty()
    {
        var store = new FakeDocumentStore();

        var results = await store.SearchAsync(
            "ghost",
            new RetrievalQuery { Text = "q" },
            TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }
}
