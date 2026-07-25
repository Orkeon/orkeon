using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRaggableStore"/>: serves canned semantic
/// hits and records the semantic queries received (everything else is inert).
/// </summary>
public sealed class RecordingRaggableStore : IRaggableStore
{
    private readonly IReadOnlyList<SearchHit> _hits;

    /// <summary>Number of <see cref="SemanticSearchAsync"/> calls.</summary>
    public int SemanticCalls { get; private set; }

    /// <summary>Last semantic query received.</summary>
    public SemanticQuery? LastQuery { get; private set; }

    public RecordingRaggableStore(IReadOnlyList<SearchHit> hits)
    {
        _hits = hits;
    }

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
