using Microsoft.Extensions.AI;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>
/// <see cref="IRagRetrievalCapable.RetrieveAsync"/> on
/// <see cref="StagedRagPipeline"/>: the retrieval half of a query, without the
/// generation the caller would have paid for and discarded.
/// </summary>
/// <remarks>
/// The point every test here defends is that "same passages, no LLM call" is a
/// guarantee and not a hope. The measurement that motivated the surface — exp02's
/// round-41, seven generations discarded by design at 13 748 completion tokens and
/// 393 s — is only recovered if the citations are IDENTICAL to the query path's;
/// a retrieve that returned different passages would just be a second, cheaper
/// pipeline with its own behaviour.
/// </remarks>
public class StagedRagPipelineRetrieveTests
{
    private static EmbeddedChunk MakeChunk(string id, string content, float[] embedding) => new()
    {
        Chunk = new Chunk
        {
            Id = id,
            DocumentId = "doc-1",
            SourceId = "/kb/doc.txt",
            Content = content,
            StartOffset = 0,
            EndOffset = content.Length,
        },
        Embedding = [.. embedding],
    };

    private static async Task<FakeDocumentStore> SeedStoreAsync(params EmbeddedChunk[] chunks)
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync("kb", chunks, TestContext.Current.CancellationToken);
        return store;
    }

    private static Task<FakeDocumentStore> SeedRankedStoreAsync() => SeedStoreAsync(
        MakeChunk("r1", "rank one", [1f, 1f, 1f, 1f]),
        MakeChunk("r2", "rank two", [1f, 1f, 1f, 0.5f]),
        MakeChunk("r3", "rank three", [1f, 1f, 0.5f, 0.5f]),
        MakeChunk("r4", "rank four", [1f, 0.5f, 0.5f, 0.5f]),
        MakeChunk("r5", "rank five", [1f, 0.2f, 0.2f, 0.2f]));

    [Fact]
    public async Task RetrieveAsync_CostsNoChatCall()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient { ResponseText = "must never be produced" };
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.RetrieveAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 3 },
            TestContext.Current.CancellationToken);

        Assert.Equal(0, chat.CallCount);
        Assert.Equal(string.Empty, answer.Text);
        Assert.Equal(3, answer.Citations.Count);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsTheSameCitationsAsQueryAsync()
    {
        // The whole value of the surface: identical evidence, one fewer paid stage.
        var queryStore = await SeedRankedStoreAsync();
        using var queryChat = new FakeChatClient { ResponseText = "Grounded [1]." };
        var queried = await new StagedRagPipeline(queryStore, new FakeEmbeddingProvider(), queryChat)
            .QueryAsync(
                new RagQuery { Text = "rank?", Collection = "kb", TopN = 4 },
                TestContext.Current.CancellationToken);

        var retrieveStore = await SeedRankedStoreAsync();
        using var retrieveChat = new FakeChatClient();
        var retrieved = await new StagedRagPipeline(retrieveStore, new FakeEmbeddingProvider(), retrieveChat)
            .RetrieveAsync(
                new RagQuery { Text = "rank?", Collection = "kb", TopN = 4 },
                TestContext.Current.CancellationToken);

        Assert.Equal(
            queried.Citations.Select(c => (c.Marker, c.ChunkId, c.Snippet)),
            retrieved.Citations.Select(c => (c.Marker, c.ChunkId, c.Snippet)));
        Assert.Equal(1, queryChat.CallCount);
        Assert.Equal(0, retrieveChat.CallCount);
    }

    [Fact]
    public async Task RetrieveAsync_TracesTheSkippedGenerationRatherThanOmittingIt()
    {
        // An absent `generate` step would read as a trace from an older pipeline.
        // The stage must say it was skipped, and why.
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.RetrieveAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["transform", "retrieve", "fuse", "rerank", "assemble", "generate"],
            answer.Trace.Steps.Select(s => s.Name));
        var generate = answer.Trace.Steps[^1];
        Assert.Contains("skipped", generate.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retrieval-only", generate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetrieveAsync_OnEmptyCollection_ReturnsNoCitationsAndNoProseApology()
    {
        // QueryAsync answers "no relevant context was found" in prose. A
        // retrieve-only caller reads citations, and that sentence sitting in
        // `text` would be indistinguishable from a retrieved passage.
        var store = new FakeDocumentStore();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.RetrieveAsync(
            new RagQuery { Text = "anything?", Collection = "kb", TopN = 3 },
            TestContext.Current.CancellationToken);

        Assert.Empty(answer.Citations);
        Assert.Equal(string.Empty, answer.Text);
        Assert.Equal(0, chat.CallCount);
        Assert.Contains(
            answer.Trace.Steps,
            s => s.Name == "generate"
                 && (s.Detail ?? "").Contains("no retrieved context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryAsync_StillGenerates_AfterTheRetrieveRefactor()
    {
        // Regression guard on the shared core: QueryAsync and RetrieveAsync now
        // run the same stages 1-5, so a mistake in the split would silently turn
        // every query into a retrieve.
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient { ResponseText = "Grounded answer [1]." };
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal("Grounded answer [1].", answer.Text);
        Assert.Equal(1, chat.CallCount);
    }

    [Fact]
    public async Task RetrieveAsync_HonoursTheRerankStage()
    {
        // Retrieval-only must not quietly become "vector search only": the stages
        // that shape WHICH passages come back all still run.
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var reranker = new ReversingReranker();
        var rerankers = new Orkeon.Rag.Factories.RerankerFactory();
        rerankers.Register(ReversingReranker.RerankerName, () => reranker);
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Rerank = new RagRerankOptions { Enabled = true, Kind = ReversingReranker.RerankerName } },
            rerankers: rerankers);

        var answer = await pipeline.RetrieveAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 3 },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, reranker.CallCount);
        Assert.Contains(answer.Trace.Steps, s => s.Name == "rerank");
        Assert.Equal(3, answer.Citations.Count);
    }

    /// <summary>Reranker that reverses the candidate order — observable and deterministic.</summary>
    private sealed class ReversingReranker : IReranker
    {
        public const string RerankerName = "reversing";

        public string Name => RerankerName;

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
            string query,
            IReadOnlyList<ScoredChunk> candidates,
            int topN,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<ScoredChunk> reversed = [.. candidates.Reverse().Take(topN)];
            return Task.FromResult(reversed);
        }
    }
}
