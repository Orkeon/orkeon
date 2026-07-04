using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Core.ContextInjection;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

public class CodebaseContextProviderTests
{
    private static async Task<(InMemoryRaggableStore Store, string Dir)> BuildStoreAsync(
        (string Path, string Content)[] files)
    {
        var dir = await TestFixtures.WriteDirectoryAsync(files);
        var fs = TestFixtures.CreateFs(dir);
        var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
        var result = await builder.BuildAsync(TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
        return (new InMemoryRaggableStore(result.Tree, new FakeFileSystemService()), dir);
    }

    [Fact]
    public async Task Markdown_reports_package_file_and_symbol_totals()
    {
        var (store, dir) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
            ("src/b.ts", TestFixtures.Utils),
        ]);
        try
        {
            var provider = new CodebaseContextProvider(store);
            var text = await provider.GetContextAsync(
                new CodebaseContextOptions { Format = "markdown", TopN = 3 },
                CancellationToken.None);

            Assert.Contains("CONTEXTE CODEBASE", text, StringComparison.Ordinal);
            Assert.Contains("packages", text, StringComparison.Ordinal);
            Assert.Contains("symboles", text, StringComparison.Ordinal);
            Assert.Contains("2 fichiers .typescript", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Markdown_lists_top_complexity_and_centrality()
    {
        var (store, dir) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/calls.ts", TestFixtures.Calls),
            ("src/complex.ts", TestFixtures.ComplexMethodTs),
        ]);
        try
        {
            var provider = new CodebaseContextProvider(store);
            var text = await provider.GetContextAsync(
                new CodebaseContextOptions { TopN = 3 },
                CancellationToken.None);

            Assert.Contains("Top-", text, StringComparison.Ordinal);
            Assert.Contains("complexité", text, StringComparison.Ordinal);
            Assert.Contains("couplage", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Empty_index_returns_empty_string()
    {
        var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
        var provider = new CodebaseContextProvider(store);
        var text = await provider.GetContextAsync(
            new CodebaseContextOptions(), CancellationToken.None);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task Json_format_returns_parseable_payload()
    {
        var (store, dir) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var provider = new CodebaseContextProvider(store);
            var text = await provider.GetContextAsync(
                new CodebaseContextOptions { Format = "json" },
                CancellationToken.None);

            using var doc = JsonDocument.Parse(text);
            Assert.True(doc.RootElement.TryGetProperty("packages", out _));
            Assert.True(doc.RootElement.TryGetProperty("files", out _));
            Assert.True(doc.RootElement.TryGetProperty("symbols", out var symProp));
            Assert.True(symProp.GetInt32() > 0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Compact_format_is_short_and_contains_counts()
    {
        var (store, dir) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var provider = new CodebaseContextProvider(store);
            var text = await provider.GetContextAsync(
                new CodebaseContextOptions { Format = "compact", TopN = 3 },
                CancellationToken.None);

            Assert.StartsWith("codebase=", text, StringComparison.Ordinal);
            Assert.Contains("p/", text, StringComparison.Ordinal);
            Assert.Contains("f/", text, StringComparison.Ordinal);
            Assert.Contains("s", text, StringComparison.Ordinal);
            Assert.True(text.Length < 400, $"Compact should stay short, was {text.Length} chars: {text}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Include_patterns_false_omits_patterns_section()
    {
        var (store, dir) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var nodes = await store.QueryAsync(new Abstractions.DTOs.Queries.NodeQuery
            {
                Level = NodeLevel.L3_Symbol,
            }, CancellationToken.None);
            var first = nodes[0];
            first.TagsMutable["pattern-observer"] = "builtin";

            var provider = new CodebaseContextProvider(store);
            var with = await provider.GetContextAsync(
                new CodebaseContextOptions { IncludePatterns = true }, CancellationToken.None);
            var without = await provider.GetContextAsync(
                new CodebaseContextOptions { IncludePatterns = false }, CancellationToken.None);

            Assert.Contains("observer", with, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Patterns détectés", without, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Markdown_budget_under_500_chars_for_small_codebase()
    {
        var (store, dir) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var provider = new CodebaseContextProvider(store);
            var text = await provider.GetContextAsync(
                new CodebaseContextOptions { TopN = 5 },
                CancellationToken.None);

            Assert.InRange(text.Length, 1, 1500);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Fake_store_drives_deterministic_values()
    {
        var store = new StubStore();
        var provider = new CodebaseContextProvider(store);

        var text = await provider.GetContextAsync(
            new CodebaseContextOptions { Format = "markdown", TopN = 2 },
            CancellationToken.None);

        Assert.Contains("14 packages", text, StringComparison.Ordinal);
        Assert.Contains("333 fichiers", text, StringComparison.Ordinal);
        Assert.Contains("842 symboles", text, StringComparison.Ordinal);
        Assert.Contains("types", text, StringComparison.Ordinal);
        Assert.Contains("ActorLogic", text, StringComparison.Ordinal);
    }

    private sealed class StubStore : IRaggableStore
    {
        private readonly List<RaggableNode> _packages = Enumerable.Range(0, 14).Select(i => MakeNode($"p{i}", NodeLevel.L1_Package, "multi")).ToList();
        private readonly List<RaggableNode> _modules = Enumerable.Range(0, 333).Select(i => MakeNode($"m{i}", NodeLevel.L2_Module, "typescript")).ToList();
        private readonly List<RaggableNode> _symbols = Enumerable.Range(0, 842).Select(i => MakeNode($"s{i}", NodeLevel.L3_Symbol, "typescript")).ToList();

        public Task<int> GetNodeCountAsync(CancellationToken ct) => Task.FromResult(_packages.Count + _modules.Count + _symbols.Count);

        public Task<IReadOnlyList<RaggableNode>> QueryAsync(Abstractions.DTOs.Queries.NodeQuery query, CancellationToken ct)
        {
            IReadOnlyList<RaggableNode> list = query.Level switch
            {
                NodeLevel.L1_Package => _packages,
                NodeLevel.L2_Module => _modules,
                NodeLevel.L3_Symbol => _symbols,
                _ => [],
            };
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<Abstractions.DTOs.Responses.ComplexityEntry>> TopComplexityAsync(
            Abstractions.DTOs.Queries.ComplexityQuery query, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<Abstractions.DTOs.Responses.ComplexityEntry>>(
            [
                new("pkg::types", 42, ComplexityMetric.Cyclomatic),
                new("pkg::StateMachine", 30, ComplexityMetric.Cyclomatic),
            ]);
        }

        public Task<IReadOnlyList<Abstractions.DTOs.Responses.CentralityEntry>> TopCentralityAsync(
            Abstractions.DTOs.Queries.CentralityQuery query, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<Abstractions.DTOs.Responses.CentralityEntry>>(
            [
                new("pkg::ActorLogic", 15, CentralityMetric.InDegreeCalls),
                new("pkg::EventObject", 10, CentralityMetric.InDegreeCalls),
            ]);
        }

        public IReadOnlyList<Abstractions.Models.IndexedRoot> GetIndexedRoots() => [];

        public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);
        public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StatementNode>>([]);
        public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct) => Task.FromResult<RaggableNode?>(null);
        public Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct) => Task.FromResult<IReadOnlyList<RaggableEdge>>([]);
        public Task<Abstractions.DTOs.Responses.SubGraph> ExpandAsync(Abstractions.DTOs.Queries.ExpandQuery query, CancellationToken ct) => Task.FromResult(new Abstractions.DTOs.Responses.SubGraph { Nodes = [], Edges = [] });
        public Task<IReadOnlyList<Abstractions.DTOs.Responses.CallPath>> FindAllPathsAsync(Abstractions.DTOs.Queries.PathQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<Abstractions.DTOs.Responses.CallPath>>([]);
        public Task<Abstractions.DTOs.Responses.CallPath?> ShortestPathAsync(Abstractions.DTOs.Queries.ShortestPathQuery query, CancellationToken ct) => Task.FromResult<Abstractions.DTOs.Responses.CallPath?>(null);
        public Task<IReadOnlyList<Abstractions.DTOs.Responses.Cycle>> FindCyclesAsync(Abstractions.DTOs.Queries.CycleQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<Abstractions.DTOs.Responses.Cycle>>([]);
        public Task<IReadOnlyList<Abstractions.DTOs.Responses.SearchHit>> SemanticSearchAsync(Abstractions.DTOs.Queries.SemanticQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<Abstractions.DTOs.Responses.SearchHit>>([]);
        public Task<Abstractions.DTOs.Responses.SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct) => Task.FromResult<Abstractions.DTOs.Responses.SourceSlice?>(null);

        private static RaggableNode MakeNode(string id, NodeLevel level, string language) => new()
        {
            Id = id,
            Kind = level == NodeLevel.L1_Package ? UniversalNodeKind.Package
                : level == NodeLevel.L2_Module ? UniversalNodeKind.Module
                : UniversalNodeKind.Function,
            Name = id,
            VirtualFilePath = id,
            Range = new NodeRange(0, 0, 0, 0, 0),
            Level = level,
            Language = language,
            SourceSnippet = string.Empty,
            Sha256 = string.Empty,
            Fqn = id,
        };
    }
}
