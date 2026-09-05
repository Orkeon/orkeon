using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="StagedRagPipeline"/> (RAG-04/C4): stage sequence and
/// trace, rerank cascade (CandidateK in → TopN out through the named reranker),
/// anti-Lost-in-the-Middle <c>edges</c> context ordering with stable rank-based
/// markers, and the groundedness hook.
/// </summary>
public class StagedRagPipelineTests
{
    private static readonly string[] s_edgeOrderContents =
        ["rank one", "rank three", "rank five", "rank four", "rank two"];

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

    /// <summary>Five chunks with strictly decreasing cosine alignment to the [1,1,1,1] query vector.</summary>
    private static Task<FakeDocumentStore> SeedRankedStoreAsync() => SeedStoreAsync(
        MakeChunk("r1", "rank one", [1f, 1f, 1f, 1f]),
        MakeChunk("r2", "rank two", [1f, 1f, 1f, 0.5f]),
        MakeChunk("r3", "rank three", [1f, 1f, 0.5f, 0.5f]),
        MakeChunk("r4", "rank four", [1f, 0.5f, 0.5f, 0.5f]),
        MakeChunk("r5", "rank five", [1f, 0.2f, 0.2f, 0.2f]));

    // ── baseline behaviour (carried over from the linear pipeline) ─────────

    [Fact]
    public async Task QueryAsync_ReturnsAnswerWithCitationsAndTrace()
    {
        var store = await SeedStoreAsync(
            MakeChunk("a", "alpha content", [1f, 1f, 1f, 1f]),
            MakeChunk("b", "beta content", [-1f, 1f, -1f, 1f]));
        using var chat = new FakeChatClient { ResponseText = "Grounded answer [1].", ResponseModelId = "fake-model" };
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb", TopN = 1 },
            TestContext.Current.CancellationToken);

        Assert.Equal("Grounded answer [1].", answer.Text);

        var citation = Assert.Single(answer.Citations);
        Assert.Equal(1, citation.Marker);
        Assert.Equal("a", citation.ChunkId);
        Assert.Equal("/kb/doc.txt", citation.SourceId);
        Assert.Equal("alpha content", citation.Snippet);
        Assert.True(citation.Score > 0.9); // cosine score survived end-to-end

        Assert.Equal(
            ["transform", "retrieve", "fuse", "rerank", "assemble", "generate"],
            answer.Trace.Steps.Select(s => s.Name));
        Assert.Equal("fake-model", answer.Trace.Steps[^1].Data["model"]);
    }

    [Fact]
    public async Task QueryAsync_RetrievalUsesQueryEmbeddingAndCandidateK()
    {
        var store = await SeedStoreAsync(MakeChunk("a", "alpha", [1f, 1f, 1f, 1f]));
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Retrieval = new RagRetrievalOptions { CandidateK = 7 } });

        await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        var call = Assert.Single(store.SearchCalls);
        Assert.Equal(7, call.TopK);
        Assert.Equal("alpha?", call.Text);
        Assert.NotNull(call.Embedding);
        Assert.False(call.Hybrid); // hybrid disabled by default
    }

    [Fact]
    public async Task QueryAsync_HybridEnabled_SetsTheQueryHybridFlag()
    {
        var store = await SeedStoreAsync(MakeChunk("a", "alpha", [1f, 1f, 1f, 1f]));
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions
            {
                Retrieval = new RagRetrievalOptions { Hybrid = new RagHybridOptions { Enabled = true } },
            });

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        var call = Assert.Single(store.SearchCalls);
        Assert.True(call.Hybrid);

        var step = Assert.Single(answer.Trace.Steps, s => s.Name == "retrieve");
        Assert.Equal("true", step.Data["hybrid"]);
    }

    [Fact]
    public async Task QueryAsync_FiltersArePropagatedToTheStore()
    {
        var store = new FakeDocumentStore();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        await pipeline.QueryAsync(
            new RagQuery
            {
                Text = "q",
                Collection = "kb",
                Filters = ImmutableDictionary<string, string>.Empty.Add("lang", "fr"),
            },
            TestContext.Current.CancellationToken);

        var call = Assert.Single(store.SearchCalls);
        Assert.Equal("fr", call.Filters["lang"]);
    }

    [Fact]
    public async Task QueryAsync_NoCandidates_SkipsGenerationAndReturnsDeterministicAnswer()
    {
        var store = new FakeDocumentStore(); // empty collection
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "anything?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StagedRagPipeline.NoContextAnswer, answer.Text);
        Assert.Empty(answer.Citations);
        Assert.Equal(0, chat.CallCount);
        Assert.Contains(answer.Trace.Steps, s => s.Name == "generate" && s.Detail is not null);
    }

    [Fact]
    public async Task QueryAsync_GenerationOptions_ReachTheChatClient()
    {
        var store = await SeedStoreAsync(MakeChunk("a", "alpha", [1f, 1f, 1f, 1f]));
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions
            {
                Generation = new RagGenerationOptions { Temperature = 0.2f, MaxOutputTokens = 123 },
            });

        await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(chat.LastOptions);
        Assert.Equal(0.2f, chat.LastOptions.Temperature);
        Assert.Equal(123, chat.LastOptions.MaxOutputTokens);
        Assert.Equal(ChatRole.System, chat.LastMessages![0].Role);
        Assert.Equal(StagedRagPipeline.DefaultSystemPrompt, chat.LastMessages[0].Text);
    }

    // ── rerank cascade ─────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_RerankEnabled_ReceivesAllCandidates_AndKeepsTopN()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var reranker = new RecordingReranker();
        var rerankers = new RerankerFactory();
        rerankers.Register("recording", () => reranker);

        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions
            {
                Retrieval = new RagRetrievalOptions { CandidateK = 50 },
                Rerank = new RagRerankOptions { Enabled = true, Kind = "recording" },
            },
            new StagedRagPipelineDependencies { Rerankers = rerankers });

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        // The cascade: all 5 candidates in, TopN = 2 out.
        var call = Assert.Single(reranker.Calls);
        Assert.Equal("rank?", call.Query);
        Assert.Equal(5, call.CandidateCount);
        Assert.Equal(2, call.TopN);

        // The reranker's ordering wins: it reverses, so r5 is now the best chunk.
        Assert.Equal(2, answer.Citations.Count);
        Assert.Equal("r5", answer.Citations[0].ChunkId);
        Assert.Equal("r4", answer.Citations[1].ChunkId);

        var step = Assert.Single(answer.Trace.Steps, s => s.Name == "rerank");
        Assert.Equal("recording", step.Data["reranker"]);
        Assert.Equal("5", step.Data["candidates"]);
        Assert.Equal("2", step.Data["kept"]);
    }

    [Fact]
    public async Task QueryAsync_RerankDisabled_TruncatesInRetrievalOrder()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 3 },
            TestContext.Current.CancellationToken);

        Assert.Equal(["r1", "r2", "r3"], answer.Citations.Select(c => c.ChunkId));
        var step = Assert.Single(answer.Trace.Steps, s => s.Name == "rerank");
        Assert.Equal("none", step.Data["reranker"]);
        Assert.NotNull(step.Detail);
    }

    [Fact]
    public void Ctor_RerankEnabledWithoutFactory_FailsLoudly()
    {
        using var chat = new FakeChatClient();
        var ex = Assert.Throws<InvalidOperationException>(() => new StagedRagPipeline(
            new FakeDocumentStore(),
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Rerank = new RagRerankOptions { Enabled = true, Kind = "onnx" } }));

        Assert.Contains("RerankerFactory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_UnknownReranker_FailsLoudly_ListingKnownNames()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var rerankers = new RerankerFactory();
        rerankers.Register("recording", () => new RecordingReranker());

        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Rerank = new RagRerankOptions { Enabled = true, Kind = "onnx" } },
            new StagedRagPipelineDependencies { Rerankers = rerankers });

        var ex = await Assert.ThrowsAsync<RagComponentNotFoundException>(() => pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb" },
            TestContext.Current.CancellationToken));

        Assert.Contains("onnx", ex.Message, StringComparison.Ordinal);
        Assert.Contains("recording", ex.Message, StringComparison.Ordinal);
    }

    // ── anti-Lost-in-the-Middle assembly ───────────────────────────────────

    [Fact]
    public void ComputeContextOrder_Edges_PlacesBestRanksAtTheExtremities()
    {
        // 1-based ranks: odd ranks open the block, even ranks close it in reverse.
        Assert.Equal([0], StagedRagPipeline.ComputeContextOrder(1, edges: true));
        Assert.Equal([0, 1], StagedRagPipeline.ComputeContextOrder(2, edges: true));
        Assert.Equal([0, 2, 1], StagedRagPipeline.ComputeContextOrder(3, edges: true));
        Assert.Equal([0, 2, 3, 1], StagedRagPipeline.ComputeContextOrder(4, edges: true));
        Assert.Equal([0, 2, 4, 3, 1], StagedRagPipeline.ComputeContextOrder(5, edges: true));
        Assert.Equal([0, 2, 4, 5, 3, 1], StagedRagPipeline.ComputeContextOrder(6, edges: true));
    }

    [Fact]
    public void ComputeContextOrder_Linear_IsPlainRankOrder()
    {
        Assert.Equal([0, 1, 2, 3], StagedRagPipeline.ComputeContextOrder(4, edges: false));
    }

    [Fact]
    public async Task QueryAsync_EdgesOrdering_LaysOutContext_BestAtHeadAndTail_WithRankStableMarkers()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 5 },
            TestContext.Current.CancellationToken);

        var user = chat.LastMessages![^1].Text!;

        // Document order in the context block: ranks 1, 3, 5, 4, 2.
        var positions = s_edgeOrderContents
            .Select(content => user.IndexOf(content, StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.True(positions.SequenceEqual(positions.Order()),
            $"context order is not 1,3,5,4,2 — offsets: {string.Join(", ", positions)}");

        // Markers stay rank-based: [1] labels the best chunk wherever it sits.
        Assert.Contains("[1] (source:", user, StringComparison.Ordinal);
        Assert.True(user.IndexOf("[1]", StringComparison.Ordinal) < user.IndexOf("[3]", StringComparison.Ordinal));
        Assert.True(user.IndexOf("[4]", StringComparison.Ordinal) < user.IndexOf("[2]", StringComparison.Ordinal));

        // Citations are rank-ordered regardless of the layout.
        Assert.Equal(["r1", "r2", "r3", "r4", "r5"], answer.Citations.Select(c => c.ChunkId));
        Assert.Equal([1, 2, 3, 4, 5], answer.Citations.Select(c => c.Marker));

        var step = Assert.Single(answer.Trace.Steps, s => s.Name == "assemble");
        Assert.Equal("edges", step.Data["ordering"]);
    }

    [Fact]
    public async Task QueryAsync_LinearOrdering_KeepsRankOrderInTheContext()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Context = new RagContextOptions { Ordering = "linear" } });

        await pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb", TopN = 3 },
            TestContext.Current.CancellationToken);

        var user = chat.LastMessages![^1].Text!;
        Assert.True(user.IndexOf("rank one", StringComparison.Ordinal)
            < user.IndexOf("rank two", StringComparison.Ordinal));
        Assert.True(user.IndexOf("rank two", StringComparison.Ordinal)
            < user.IndexOf("rank three", StringComparison.Ordinal));
    }

    [Fact]
    public void Ctor_UnknownOrdering_FailsLoudly()
    {
        using var chat = new FakeChatClient();
        var ex = Assert.Throws<InvalidOperationException>(() => new StagedRagPipeline(
            new FakeDocumentStore(),
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Context = new RagContextOptions { Ordering = "sandwich" } }));

        Assert.Contains("edges", ex.Message, StringComparison.Ordinal);
        Assert.Contains("linear", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_ContextBudget_DropsChunksBeyondTheTokenBudget()
    {
        var big = new string('x', 300); // 300 chars ≈ 75 tokens each
        var store = await SeedStoreAsync(
            MakeChunk("r1", big, [1f, 1f, 1f, 1f]),
            MakeChunk("r2", big, [1f, 1f, 1f, 0.5f]),
            MakeChunk("r3", big, [1f, 1f, 0.5f, 0.5f]));
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Context = new RagContextOptions { MaxTokens = 160 } }); // 640 chars → 2 chunks

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "x?", Collection = "kb", TopN = 3 },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, answer.Citations.Count);
        var step = Assert.Single(answer.Trace.Steps, s => s.Name == "assemble");
        Assert.Equal("1", step.Data["dropped_by_budget"]);
    }

    // ── hooks ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_GroundednessEnabledWithoutChecker_TracesTheSkip()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { Groundedness = new RagGroundednessOptions { Enabled = true } });

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        Assert.Null(answer.Groundedness);
        var step = Assert.Single(answer.Trace.Steps, s => s.Name == "groundedness");
        Assert.Contains("RAG-06", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Ctor_TransformModeWithoutFactory_FailsLoudly()
    {
        using var chat = new FakeChatClient();
        var ex = Assert.Throws<InvalidOperationException>(() => new StagedRagPipeline(
            new FakeDocumentStore(),
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions { QueryTransform = new RagQueryTransformOptions { Mode = "multi-query" } }));

        Assert.Contains("QueryTransformerFactory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_TransformNone_IsTracedAsPassthrough()
    {
        var store = await SeedRankedStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "rank?", Collection = "kb" },
            TestContext.Current.CancellationToken);

        var step = Assert.Single(answer.Trace.Steps, s => s.Name == "transform");
        Assert.Equal("none", step.Data["mode"]);
        Assert.Empty(answer.Trace.QueryVariants);
    }
}
