using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Analysis.Tests;

public class CodebaseSearchToolTests
{
    private static readonly float[] AlignedVector = [1f, 0f, 0f];
    private static readonly float[] OrthogonalVector = [0f, 1f, 0f];

    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new CodebaseSearchTool(TestGraph.Store([], []));
        Assert.Equal("codebase_search", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Without_embedder_the_hybrid_default_degrades_to_bm25()
    {
        // Pre-hybrid this returned [] — a configured-out embedder made every search
        // read as an empty codebase, silently. The lexical half now answers, and the
        // hit says which ranking produced it.
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts");
        var store = TestGraph.Store([sym], []);
        using var tool = new CodebaseSearchTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle request", TopK = 5 },
            CancellationToken.None);

        var hit = Assert.Single(resp.Hits);
        Assert.Equal("bm25", hit.MatchOrigin);
    }

    [Fact]
    public async Task Explicit_Vector_mode_without_embedder_keeps_the_historical_empty()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts");
        var store = TestGraph.Store([sym], []);
        using var tool = new CodebaseSearchTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle request", TopK = 5, Mode = SearchMode.Vector },
            CancellationToken.None);

        Assert.Empty(resp.Hits);
        Assert.Equal(0, resp.TotalCandidates);
    }

    [Fact]
    public async Task Returns_ranked_hit_with_fqn_and_score_when_embeddings_match()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts", signature: "handle(): void");
        sym.Embedding = new float[] { 1f, 0f, 0f };
        var store = TestGraph.Store([sym], []);
        store.SetQueryEmbedder((_, _) => Task.FromResult<ReadOnlyMemory<float>?>(AlignedVector));
        using var tool = new CodebaseSearchTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle", TopK = 5, IncludeSignature = true },
            CancellationToken.None);

        // Both halves match "handle": the hit is FUSED — origin says so, and the score
        // is an RRF rank aggregate, not a cosine (the >0.99 pin belongs to Vector mode).
        var hit = Assert.Single(resp.Hits);
        Assert.Equal("/src::handle", hit.Fqn);
        Assert.Equal("hybrid", hit.MatchOrigin);
        Assert.Equal("handle(): void", hit.Signature);
    }

    [Fact]
    public async Task Vector_mode_keeps_the_cosine_scale()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts", signature: "handle(): void");
        sym.Embedding = new float[] { 1f, 0f, 0f };
        var store = TestGraph.Store([sym], []);
        store.SetQueryEmbedder((_, _) => Task.FromResult<ReadOnlyMemory<float>?>(AlignedVector));
        using var tool = new CodebaseSearchTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle", TopK = 5, Mode = SearchMode.Vector },
            CancellationToken.None);

        var hit = Assert.Single(resp.Hits);
        Assert.True(hit.Score > 0.99);
        Assert.Equal("vector", hit.MatchOrigin);
    }

    [Fact]
    public async Task Filters_out_hits_below_min_score()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts");
        sym.Embedding = new float[] { 1f, 0f, 0f };
        var store = TestGraph.Store([sym], []);
        // Orthogonal query vector -> cosine 0, below any positive MinScore.
        store.SetQueryEmbedder((_, _) => Task.FromResult<ReadOnlyMemory<float>?>(OrthogonalVector));
        using var tool = new CodebaseSearchTool(store);

        // MinScore floors the VECTOR half only (cosine scale — RRF aggregates live on
        // another scale, documented on SemanticQuery). The lexical half still answers,
        // labelled bm25; Vector mode pins the historical all-empty.
        var hybrid = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle", TopK = 5, MinScore = 0.5 },
            CancellationToken.None);
        var hit = Assert.Single(hybrid.Hits);
        Assert.Equal("bm25", hit.MatchOrigin);

        var vector = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle", TopK = 5, MinScore = 0.5, Mode = SearchMode.Vector },
            CancellationToken.None);
        Assert.Empty(vector.Hits);
    }

    [Fact]
    public async Task Strips_signature_when_not_requested()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts", signature: "handle(): void");
        sym.Embedding = new float[] { 1f, 0f, 0f };
        var store = TestGraph.Store([sym], []);
        store.SetQueryEmbedder((_, _) => Task.FromResult<ReadOnlyMemory<float>?>(AlignedVector));
        using var tool = new CodebaseSearchTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle", TopK = 5, IncludeSignature = false },
            CancellationToken.None);

        var hit = Assert.Single(resp.Hits);
        Assert.Null(hit.Signature);
    }
}
