using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Persists and queries the stratified RaggableTree graph.
/// </summary>
public interface IRaggableStore
{
    Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct);
    Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct);

    /// <summary>
    /// Finds nodes whose FQN ends with <c>::<paramref name="localName"/></c> — i.e. nodes
    /// that share a local symbol name regardless of their containing module/package.
    /// Used by <see cref="IInlineFqnValidator"/> to resolve bare-form citations such as
    /// <c>ts::MySqlDialect</c> against the canonical long FQN.
    /// </summary>
    /// <param name="localName">The trailing symbol name (segment after the last <c>::</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Zero or more nodes matching the trailing symbol name.</returns>
    Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct);
    Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct);
    Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct);
    Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct);

    Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct);
    Task<int> GetNodeCountAsync(CancellationToken ct);

    Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct);
    Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct);
    Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct);
    Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct);
    Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct);

    Task<IReadOnlyList<SearchHit>> SemanticSearchAsync(SemanticQuery query, CancellationToken ct);

    Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct);

    Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct);
    Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct);

    IReadOnlyList<IndexedRoot> GetIndexedRoots();
}
