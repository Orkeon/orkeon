using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Application.Interfaces.Rag;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Knowledge;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge;

public class RagToolCollectionTests
{
    [Fact]
    public async Task CallAsync_routes_to_raggable_store_when_collection_matches()
    {
        var pipeline = new MockRagPipeline();
        var store = new RecordingStore(
            [new SearchHit { Fqn = "pkg.Foo", Score = 0.91 }]);

        var tool = new RagTool(pipeline, store);

        var resp = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?>
            {
                ["question"] = "where is Foo declared?",
                ["top_k"] = 5,
                ["collection"] = "raggable-tree",
            }), TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        Assert.Equal(1, store.SemanticCalls);
        Assert.Equal("where is Foo declared?", store.LastQuery?.Text);
        Assert.Equal(5, store.LastQuery?.TopK);
        Assert.Contains("pkg.Foo", resp.Result!.ToString()!);
    }

    [Fact]
    public async Task CallAsync_uses_pipeline_when_collection_is_unset()
    {
        var pipeline = new MockRagPipeline();
        pipeline.SetExecuteResult(new Orkeon.Application.Rag.RagResult
        {
            Answer = "pipeline answer",
            Sources = [],
        });
        var store = new RecordingStore([]);

        var tool = new RagTool(pipeline, store);

        var resp = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "generic" }), TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        Assert.Equal(0, store.SemanticCalls);
        Assert.Contains("pipeline answer", resp.Result!.ToString()!);
    }

    [Fact]
    public async Task SearchAsync_returns_store_hits_for_raggable_tree_collection()
    {
        var pipeline = new MockRagPipeline();
        var store = new RecordingStore(
            [
                new SearchHit { Fqn = "pkg.A", Score = 0.9, SummaryShort = "class A" },
                new SearchHit { Fqn = "pkg.B", Score = 0.7, Signature = "func B()" },
            ]);

        var tool = new RagTool(pipeline, store);
        RagTool rag = tool;

        var hits = await rag.SearchAsync(new RagToolQuery
        {
            Text = "A or B",
            TopK = 8,
            Collection = "raggable-tree",
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, hits.Count);
        Assert.Equal("pkg.A", hits[0].SourceId);
        Assert.Equal("class A", hits[0].Content);
        Assert.Equal("pkg.B", hits[1].SourceId);
        Assert.Equal("func B()", hits[1].Content);
    }

    private sealed class RecordingStore : IRaggableStore
    {
        private readonly IReadOnlyList<SearchHit> _hits;
        public int SemanticCalls { get; private set; }
        public SemanticQuery? LastQuery { get; private set; }

        public RecordingStore(IReadOnlyList<SearchHit> hits) { _hits = hits; }

        public Task<IReadOnlyList<SearchHit>> SemanticSearchAsync(SemanticQuery query, CancellationToken ct)
        {
            SemanticCalls++;
            LastQuery = query;
            return Task.FromResult(_hits);
        }

        public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);
        public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StatementNode>>([]);
        public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);
        public Task<int> GetNodeCountAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableEdge>>([]);
        public Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct) => Task.FromResult(new SubGraph { Nodes = ImmutableArray<RaggableNode>.Empty, Edges = ImmutableArray<RaggableEdge>.Empty, Truncated = false });
        public Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<CallPath>>([]);
        public Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct) => Task.FromResult<CallPath?>(null);
        public Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<Cycle>>([]);
        public Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct) => Task.FromResult<SourceSlice?>(null);
        public Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<ComplexityEntry>>([]);
        public Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<CentralityEntry>>([]);
        public IReadOnlyList<IndexedRoot> GetIndexedRoots() => [];
    }
}
