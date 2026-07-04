using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class FileReadToolCacheTests : IDisposable
{
    private readonly string _testDir;

    public FileReadToolCacheTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"fileread_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UseRaggableCache_hit_returns_indexed_snippet_when_hash_matches()
    {
        var path = Path.Combine(_testDir, "a.ts");
        var disk = "export const X = 1;\n";
        await File.WriteAllTextAsync(path, disk, TestContext.Current.CancellationToken);

        var cachedSnippet = "// FROM INDEX\nexport const X = 1;\n";
        var store = new StubStore(
            modulesByPath: new() { [path] = (Sha256: Sha256OfUtf8(disk), Snippet: cachedSnippet) });

        using var tool = new FileReadTool(FakeFs(), FakePv(), store);
        var dict = await CallAndGetDictAsync(tool, path, useCache: true);

        Assert.Equal(cachedSnippet, dict["content"]?.ToString());
        Assert.Equal(true, dict["served_from_cache"]);
    }

    [Fact]
    public async Task UseRaggableCache_miss_falls_back_to_disk_when_hash_mismatch()
    {
        var path = Path.Combine(_testDir, "b.ts");
        var disk = "export const X = 2;\n";
        await File.WriteAllTextAsync(path, disk, TestContext.Current.CancellationToken);

        var store = new StubStore(
            modulesByPath: new() { [path] = (Sha256: "deadbeef", Snippet: "stale cached content") });

        using var tool = new FileReadTool(FakeFs(), FakePv(), store);
        var dict = await CallAndGetDictAsync(tool, path, useCache: true);

        Assert.Equal(disk, dict["content"]?.ToString());
        Assert.Equal(false, dict["served_from_cache"]);
    }

    [Fact]
    public async Task UseRaggableCache_without_store_reads_from_disk()
    {
        var path = Path.Combine(_testDir, "c.ts");
        await File.WriteAllTextAsync(path, "plain", TestContext.Current.CancellationToken);

        using var tool = new FileReadTool(FakeFs(), FakePv(), raggableStore: null);
        var dict = await CallAndGetDictAsync(tool, path, useCache: true);

        Assert.Equal("plain", dict["content"]?.ToString());
        Assert.Equal(false, dict["served_from_cache"]);
    }

    [Fact]
    public async Task UseRaggableCache_false_never_queries_the_store()
    {
        var path = Path.Combine(_testDir, "d.ts");
        await File.WriteAllTextAsync(path, "raw", TestContext.Current.CancellationToken);
        var store = new StubStore(modulesByPath: []);

        using var tool = new FileReadTool(FakeFs(), FakePv(), store);
        var dict = await CallAndGetDictAsync(tool, path, useCache: false);

        Assert.Equal("raw", dict["content"]?.ToString());
        Assert.Equal(0, store.GetModuleByPathCalls);
    }

    private static async Task<Dictionary<string, object?>> CallAndGetDictAsync(FileReadTool tool, string path, bool useCache)
    {
        var resp = await tool.CallAsync(new ToolCallRequest(
            "file_read",
            new Dictionary<string, object?> { ["path"] = path, ["use_raggable_cache"] = useCache }));
        Assert.True(resp.Success, resp.Error ?? "expected success");
        var dict = resp.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict!;
    }

    private static string Sha256OfUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static PassThroughFileSystemService FakeFs() => new();

    private static StubPathValidator FakePv() => new StubPathValidator().AllowAll();

    private sealed class StubStore : IRaggableStore
    {
        private readonly Dictionary<string, (string Sha256, string Snippet)> _modulesByPath;
        public int GetModuleByPathCalls { get; private set; }

        public StubStore(Dictionary<string, (string Sha256, string Snippet)> modulesByPath)
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
}
