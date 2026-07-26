using Microsoft.Extensions.AI;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.QueryTransform;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Pipeline;

/// <summary>
/// Tests for the transform-stage wiring of <see cref="StagedRagPipeline"/>
/// (RAG-05/5D): retrieval semantics per <c>QueryTransformKind</c> —
/// <c>Union</c> (per-variant retrieval, chunk-id union, max score on
/// duplicates), <c>Fusion</c> (per-variant retrieval, RRF), <c>Replacement</c>
/// (HyDE probe substitutes the question; generation cites the ORIGINAL
/// question) — plus the opt-in MMR diversification at the fuse stage, all with
/// hand-written stub LLM/store doubles.
/// </summary>
public class StagedRagPipelineTransformTests
{
    private static EmbeddedChunk MakeChunk(string id, string content, float[] embedding) => new()
    {
        Chunk = new Chunk
        {
            Id = id,
            DocumentId = "doc-1",
            SourceId = $"/kb/{id}.txt",
            Content = content,
            StartOffset = 0,
            EndOffset = content.Length,
        },
        Embedding = [.. embedding],
    };

    /// <summary>Two orthogonal chunks: only "a" aligns with axis 1, only "b" with axis 2.</summary>
    private static async Task<FakeDocumentStore> SeedOrthogonalStoreAsync()
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync(
            "kb",
            [
                MakeChunk("a", "alpha content", [1f, 0f, 0f, 0f]),
                MakeChunk("b", "beta content", [0f, 1f, 0f, 0f]),
            ],
            TestContext.Current.CancellationToken);
        return store;
    }

    /// <summary>Embeds the original question on axis 1 and every other text on axis 2.</summary>
    private static FakeEmbeddingProvider OrthogonalEmbeddings(string originalQuery) => new()
    {
        EmbeddingFunc = text => string.Equals(text, originalQuery, StringComparison.Ordinal)
            ? [1f, 0f, 0f, 0f]
            : [0f, 1f, 0f, 0f],
    };

    private static StagedRagPipeline CreatePipeline(
        FakeDocumentStore store,
        FakeEmbeddingProvider embeddings,
        IChatClient chat,
        string mode,
        MmrOptions? mmr = null)
        => new(
            store,
            embeddings,
            chat,
            new RagOptions
            {
                QueryTransform = new RagQueryTransformOptions { Mode = mode, VariantCount = 3 },
                Retrieval = new RagRetrievalOptions { Mmr = mmr ?? new MmrOptions() },
            },
            QueryTransformFactoryDefaults.CreateDefault(() => chat));

    // ── Union (multi-query) ────────────────────────────────────────────────

    [Fact]
    public async Task MultiQuery_RetrievesPerVariant_AndUnionsByChunkId_KeepingMaxScore()
    {
        var store = await SeedOrthogonalStoreAsync();
        using var chat = new FakeChatClient { ResponseText = "1. beta variant" };
        var pipeline = CreatePipeline(store, OrthogonalEmbeddings("alpha?"), chat, "multi-query");

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        // One retrieval per text: the original question first, then the variant.
        Assert.Equal(2, store.SearchCalls.Count);
        Assert.Equal("alpha?", store.SearchCalls[0].Text);
        Assert.Equal("beta variant", store.SearchCalls[1].Text);

        // Union by chunk id, original scores kept (max on duplicates): each chunk
        // is a perfect hit for exactly one variant, so both surface at ~1.0 —
        // an RRF fusion would have crushed the scores to ~1/61.
        Assert.Equal(2, answer.Citations.Count);
        Assert.All(answer.Citations, c => Assert.True(c.Score > 0.99));

        var fuse = Assert.Single(answer.Trace.Steps, s => s.Name == "fuse");
        Assert.Equal("union", fuse.Data["method"]);

        var transform = Assert.Single(answer.Trace.Steps, s => s.Name == "transform");
        Assert.Equal("multi-query", transform.Data["transformer"]);
        Assert.Equal("union", transform.Data["kind"]);
        Assert.Equal("1", transform.Data["variants"]);
        Assert.Equal("beta variant", transform.Data["variant_1"]);
        Assert.Equal(["beta variant"], answer.Trace.QueryVariants);
    }

    // ── Fusion (rag-fusion) ────────────────────────────────────────────────

    [Fact]
    public async Task RagFusion_RetrievesPerVariant_AndFusesByRrf()
    {
        var store = await SeedOrthogonalStoreAsync();
        using var chat = new FakeChatClient { ResponseText = "1. beta variant" };
        var pipeline = CreatePipeline(store, OrthogonalEmbeddings("alpha?"), chat, "rag-fusion");

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, store.SearchCalls.Count);

        // RRF scores are rank aggregates in (0, 2/61] — not similarities.
        Assert.Equal(2, answer.Citations.Count);
        Assert.All(answer.Citations, c => Assert.True(c.Score <= 2.0 / 61 + 1e-9));

        var fuse = Assert.Single(answer.Trace.Steps, s => s.Name == "fuse");
        Assert.Equal("rrf", fuse.Data["method"]);

        var transform = Assert.Single(answer.Trace.Steps, s => s.Name == "transform");
        Assert.Equal("rag-fusion", transform.Data["transformer"]);
        Assert.Equal("fusion", transform.Data["kind"]);
    }

    // ── Replacement (HyDE) ─────────────────────────────────────────────────

    [Fact]
    public async Task Hyde_RetrievesWithTheHypotheticalDocumentOnly_AndGeneratesFromTheOriginalQuestion()
    {
        var store = await SeedOrthogonalStoreAsync();
        using var chat = new FakeChatClient { ResponseText = "A dense hypothetical passage about beta." };
        var pipeline = CreatePipeline(store, OrthogonalEmbeddings("alpha?"), chat, "hyde");

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb", TopN = 1 },
            TestContext.Current.CancellationToken);

        // The hypothetical document is the ONLY retrieval probe — the original
        // question is never retrieved with.
        var call = Assert.Single(store.SearchCalls);
        Assert.Equal("A dense hypothetical passage about beta.", call.Text);

        // The probe is embedded on axis 2 → "b" wins, proving the substitute
        // embedding drove retrieval (the question itself would have picked "a").
        var citation = Assert.Single(answer.Citations);
        Assert.Equal("b", citation.ChunkId);

        // Generation always cites the ORIGINAL user question.
        var user = chat.LastMessages![^1].Text!;
        Assert.Contains("Question: alpha?", user, StringComparison.Ordinal);

        var transform = Assert.Single(answer.Trace.Steps, s => s.Name == "transform");
        Assert.Equal("hyde", transform.Data["transformer"]);
        Assert.Equal("replacement", transform.Data["kind"]);
        Assert.Equal("1", transform.Data["variants"]);
        Assert.Equal(["A dense hypothetical passage about beta."], answer.Trace.QueryVariants);
    }

    [Fact]
    public async Task Hyde_LongHypotheticalDocument_IsTruncatedInTheTraceStep_ButNotInTheVariantsList()
    {
        var longDoc = new string('h', 500);
        var store = await SeedOrthogonalStoreAsync();
        using var chat = new FakeChatClient { ResponseText = longDoc };
        var pipeline = CreatePipeline(store, OrthogonalEmbeddings("alpha?"), chat, "hyde");

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "alpha?", Collection = "kb", TopN = 1 },
            TestContext.Current.CancellationToken);

        var transform = Assert.Single(answer.Trace.Steps, s => s.Name == "transform");
        Assert.True(transform.Data["variant_1"].Length < longDoc.Length);
        Assert.EndsWith("…", transform.Data["variant_1"], StringComparison.Ordinal);
        Assert.Equal(longDoc, Assert.Single(answer.Trace.QueryVariants));
    }

    // ── MMR at the fuse stage (opt-in) ─────────────────────────────────────

    /// <summary>Three chunks: r1/r2 near-duplicates by content, r3 lexically distinct.</summary>
    private static async Task<FakeDocumentStore> SeedRedundantStoreAsync()
    {
        var store = new FakeDocumentStore();
        await store.UpsertAsync(
            "kb",
            [
                MakeChunk("r1", "solar panel efficiency report", [1f, 1f, 1f, 1f]),
                MakeChunk("r2", "solar panel efficiency report", [1f, 1f, 1f, 0.9f]),
                MakeChunk("r3", "wind turbine maintenance costs", [1f, 1f, 0.5f, 0.5f]),
            ],
            TestContext.Current.CancellationToken);
        return store;
    }

    [Fact]
    public async Task MmrDisabled_KeepsThePureScoreOrder()
    {
        var store = await SeedRedundantStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(store, new FakeEmbeddingProvider(), chat);

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "solar?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(["r1", "r2"], answer.Citations.Select(c => c.ChunkId));
        var fuse = Assert.Single(answer.Trace.Steps, s => s.Name == "fuse");
        Assert.False(fuse.Data.ContainsKey("mmr"));
    }

    [Fact]
    public async Task MmrEnabled_DiversifiesTheFusedOrder_AndTracesLambda()
    {
        var store = await SeedRedundantStoreAsync();
        using var chat = new FakeChatClient();
        var pipeline = new StagedRagPipeline(
            store,
            new FakeEmbeddingProvider(),
            chat,
            new RagOptions
            {
                Retrieval = new RagRetrievalOptions
                {
                    Mmr = new MmrOptions { Enabled = true, Lambda = 0.3 },
                },
            });

        var answer = await pipeline.QueryAsync(
            new RagQuery { Text = "solar?", Collection = "kb", TopN = 2 },
            TestContext.Current.CancellationToken);

        // r2 duplicates r1's content (Jaccard 1) and gets pushed back at λ = 0.3:
        // the lexically distinct r3 takes the second slot.
        Assert.Equal(["r1", "r3"], answer.Citations.Select(c => c.ChunkId));

        var fuse = Assert.Single(answer.Trace.Steps, s => s.Name == "fuse");
        Assert.Equal("true", fuse.Data["mmr"]);
        Assert.Equal("0.30", fuse.Data["mmr_lambda"]);
        Assert.Equal("3", fuse.Data["mmr_in"]);
        Assert.Equal("3", fuse.Data["mmr_out"]);
    }
}
