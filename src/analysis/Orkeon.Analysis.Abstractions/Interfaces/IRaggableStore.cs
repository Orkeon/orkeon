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

    /// <summary>
    /// Lists every edge of the store whose kind is in <see cref="EdgeListQuery.Kinds"/>, one page at a time, in a
    /// deterministic order (<see cref="EdgeListKey"/>): unlike a node query followed by <see cref="GetEdgesAsync"/>,
    /// it walks the whole graph, whatever its size.
    /// </summary>
    /// <param name="query">The kinds, the optional root, the key to resume after and the page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page, the total of matching edges and whether more follow.</returns>
    Task<EdgeListPage> ListEdgesAsync(EdgeListQuery query, CancellationToken ct);
    Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct);
    Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct);
    Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct);
    Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct);

    /// <summary>
    /// Ranks the indexed nodes against <see cref="SemanticQuery.Text"/>; <see cref="SemanticSearchResult.PreFilterCapped"/>
    /// says when <see cref="SemanticQuery.PreFilter"/> matched more nodes than the store kept as candidates.
    /// </summary>
    Task<SemanticSearchResult> SemanticSearchAsync(SemanticQuery query, CancellationToken ct);

    Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct);

    Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct);
    Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct);

    IReadOnlyList<IndexedRoot> GetIndexedRoots();
}
