using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Tests.Doubles;

/// <summary>Store answering <see cref="QueryAsync"/> with <see cref="QueryResult"/> in the order given, unsorted.</summary>
internal sealed class StubUnorderedRaggableStore : IRaggableStore
{
    public List<RaggableNode> QueryResult { get; init; } = [];

    public Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RaggableNode>>(QueryResult);

    public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);

    public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RaggableNode>>([]);

    public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> GetNodeCountAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct) => throw new NotSupportedException();
    public Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct) => throw new NotSupportedException();
    public Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<SearchHit>> SemanticSearchAsync(SemanticQuery query, CancellationToken ct) => throw new NotSupportedException();
    public Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct) => throw new NotSupportedException();
    public IReadOnlyList<IndexedRoot> GetIndexedRoots() => throw new NotSupportedException();
}
