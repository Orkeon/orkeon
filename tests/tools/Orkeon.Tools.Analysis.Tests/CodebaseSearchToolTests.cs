using Orkeon.Analysis.Abstractions;
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
    public async Task Returns_no_hits_when_no_embedder_is_configured()
    {
        var sym = TestGraph.Symbol("/src::handle", "/src/a.ts");
        var store = TestGraph.Store([sym], []);
        using var tool = new CodebaseSearchTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle request", TopK = 5 },
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

        var hit = Assert.Single(resp.Hits);
        Assert.Equal("/src::handle", hit.Fqn);
        Assert.True(hit.Score > 0.99);
        Assert.Equal("handle(): void", hit.Signature);
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

        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "handle", TopK = 5, MinScore = 0.5 },
            CancellationToken.None);

        Assert.Empty(resp.Hits);
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
