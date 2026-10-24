using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>Raggable store answering <c>GetModuleByPathAsync</c> from a fixed map; every other member returns a trivial default.</summary>
public sealed class StubRaggableStore : IRaggableStore
{
    private readonly Dictionary<string, (string Sha256, string Snippet)> _modulesByPath;
    public int GetModuleByPathCalls { get; private set; }

    public StubRaggableStore(Dictionary<string, (string Sha256, string Snippet)> modulesByPath)
    {
        _modulesByPath = modulesByPath;
    }

    public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct)
    {
        GetModuleByPathCalls++;
        if (!_modulesByPath.TryGetValue(filePath, out var v))
            return Task.FromResult<RaggableNode?>(null);

        var node = new RaggableNode
        {
            Id = "m1",
            Kind = UniversalNodeKind.Module,
            Name = Path.GetFileName(filePath),
            VirtualFilePath = filePath,
            Range = new NodeRange(0, 0, 1, 1, 0),
            Level = NodeLevel.L2_Module,
            Language = "typescript",
            SourceSnippet = v.Snippet,
            Sha256 = v.Sha256,
        };
        return Task.FromResult<RaggableNode?>(node);
    }

    public Task<int> GetNodeCountAsync(CancellationToken ct) => Task.FromResult(_modulesByPath.Count);

    // Unused members — return trivial defaults.
    public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);
    public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
    public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
    public Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
    public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
    public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StatementNode>>([]);
    public Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableEdge>>([]);
    public Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct) => Task.FromResult(new SubGraph { Nodes = ImmutableArray<RaggableNode>.Empty, Edges = ImmutableArray<RaggableEdge>.Empty, Truncated = false });
    public Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<CallPath>>([]);
    public Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct) => Task.FromResult<CallPath?>(null);
    public Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<Cycle>>([]);
    public Task<IReadOnlyList<SearchHit>> SemanticSearchAsync(SemanticQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<SearchHit>>([]);
    public Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct) => Task.FromResult<SourceSlice?>(null);
    public Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<ComplexityEntry>>([]);
    public Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<CentralityEntry>>([]);
    public IReadOnlyList<Orkeon.Analysis.Abstractions.Models.IndexedRoot> GetIndexedRoots() => [];
}
