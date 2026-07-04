using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class DirectoryReadToolWarningTests : IDisposable
{
    private readonly string _testDir;

    public DirectoryReadToolWarningTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"dirread_warn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "a.txt"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Warning_emitted_when_store_has_nodes()
    {
        using var tool = new DirectoryReadTool(FakeFs(), FakePv(), new CountStore(42));
        var dict = await CallAsync(tool);
        Assert.Contains("RaggableTree", dict["warning"]?.ToString());
    }

    [Fact]
    public async Task No_warning_when_store_is_empty()
    {
        using var tool = new DirectoryReadTool(FakeFs(), FakePv(), new CountStore(0));
        var dict = await CallAsync(tool);
        Assert.False(dict.TryGetValue("warning", out var w) && w is not null);
    }

    [Fact]
    public async Task No_warning_when_no_store_injected()
    {
        using var tool = new DirectoryReadTool(FakeFs(), FakePv(), raggableStore: null);
        var dict = await CallAsync(tool);
        Assert.False(dict.TryGetValue("warning", out var w) && w is not null);
    }

    private async Task<Dictionary<string, object?>> CallAsync(DirectoryReadTool tool)
    {
        var resp = await tool.CallAsync(new ToolCallRequest(
            "directory_read",
            new Dictionary<string, object?> { ["path"] = _testDir }));
        Assert.True(resp.Success, resp.Error ?? "expected success");
        var dict = resp.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict!;
    }

    private static PassThroughFileSystemService FakeFs() => new();

    private static StubPathValidator FakePv() => new StubPathValidator().AllowAll();

    private sealed class CountStore : IRaggableStore
    {
        private readonly int _count;
        public CountStore(int count) { _count = count; }

        public Task<int> GetNodeCountAsync(CancellationToken ct) => Task.FromResult(_count);

        public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);
        public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StatementNode>>([]);
        public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);
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
}
