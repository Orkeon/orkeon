using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Tests;

public class PackageSummaryToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new PackageSummaryTool(TestGraph.Store([], []));
        Assert.Equal("package_summary", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Throws_fqn_not_found_for_unknown_package()
    {
        using var tool = new PackageSummaryTool(TestGraph.Store([], []));

        await Assert.ThrowsAsync<FqnNotFoundException>(() =>
            tool.ExecuteTypedForTest(
                new PackageSummaryRequest { Fqn = "root::missing" },
                CancellationToken.None));
    }

    [Fact]
    public async Task Summarizes_package_file_and_symbol_counts_with_exports()
    {
        var pkg = TestGraph.Package("root::a", "/src/a");
        var module = TestGraph.Module("/src/a/index.ts", parentId: pkg.Id);
        var symbol = TestGraph.Symbol(
            "root::a::Service", "/src/a/index.ts",
            kind: UniversalNodeKind.Class, parentId: module.Id);
        TestGraph.Link(pkg, module);
        TestGraph.Link(module, symbol);
        var store = TestGraph.Store([pkg, module, symbol], []);
        using var tool = new PackageSummaryTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new PackageSummaryRequest { Fqn = "root::a", IncludePublicExports = true },
            CancellationToken.None);

        Assert.Equal("root::a", resp.Fqn);
        Assert.Equal(1, resp.FileCount);
        Assert.Equal(1, resp.SymbolCount);
        Assert.Contains("root::a::Service", resp.PublicExports);
        Assert.Contains("/src/a/index.ts", resp.EntryPoints);
    }

    [Fact]
    public async Task Omits_exports_and_dependencies_when_not_requested()
    {
        var pkg = TestGraph.Package("root::a", "/src/a");
        var module = TestGraph.Module("/src/a/util.ts", parentId: pkg.Id);
        TestGraph.Link(pkg, module);
        var store = TestGraph.Store([pkg, module], []);
        using var tool = new PackageSummaryTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new PackageSummaryRequest
            {
                Fqn = "root::a",
                IncludePublicExports = false,
                IncludeDependencies = false,
            },
            CancellationToken.None);

        Assert.Empty(resp.PublicExports);
        Assert.Empty(resp.Dependencies);
    }

    [Fact]
    public async Task Reports_package_dependencies_from_import_edges()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var pkgB = TestGraph.Package("root::b", "/src/b");
        var store = TestGraph.Store(
            [pkgA, pkgB],
            [TestGraph.Edge(pkgA, pkgB, EdgeKind.Imports)]);
        using var tool = new PackageSummaryTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new PackageSummaryRequest
            {
                Fqn = "root::a",
                IncludePublicExports = false,
                IncludeDependencies = true,
            },
            CancellationToken.None);

        Assert.Contains(pkgB.Id, resp.Dependencies);
    }
}
