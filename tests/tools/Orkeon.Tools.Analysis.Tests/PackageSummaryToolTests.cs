using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Tests;

public class PackageSummaryToolTests
{
    private static readonly string[] ModulePaths =
        ["/src/a/Program.cs", "/src/a/main.go", "/src/a/__main__.py", "/src/a/DomainEvent.cs", "/src/a/Bm25Index.cs", "/src/a/MainWindow.g.cs"];

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
    public async Task Reports_the_packages_its_modules_import()
    {
        var pkgA = TestGraph.Package("root::a", "/src/a");
        var pkgB = TestGraph.Package("root::b", "/src/b");
        var a1 = TestGraph.Module("/src/a/one.ts", parentId: pkgA.Id);
        var a2 = TestGraph.Module("/src/a/two.ts", parentId: pkgA.Id);
        var b1 = TestGraph.Module("/src/b/one.ts", parentId: pkgB.Id);
        var external = TestGraph.Module("ext::lodash", language: "external");
        TestGraph.Link(pkgA, a1, a2);
        TestGraph.Link(pkgB, b1);
        var store = TestGraph.Store(
            [pkgA, pkgB, a1, a2, b1, external],
            [
                TestGraph.Edge(a1, b1, EdgeKind.Imports),
                TestGraph.Edge(a2, b1, EdgeKind.Imports),
                TestGraph.Edge(a1, a2, EdgeKind.Imports),
                TestGraph.Edge(a2, external, EdgeKind.Imports),
            ]);
        using var tool = new PackageSummaryTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new PackageSummaryRequest
            {
                Fqn = "root::a",
                IncludePublicExports = false,
                IncludeDependencies = true,
            },
            CancellationToken.None);

        Assert.Equal(["root::b"], resp.Dependencies);
    }

    [Fact]
    public async Task Reports_only_modules_named_after_an_entry_point_as_entry_points()
    {
        var pkg = TestGraph.Package("root::a", "/src/a");
        var modules = ModulePaths.Select(path => TestGraph.Module(path, parentId: pkg.Id)).ToArray();
        TestGraph.Link(pkg, modules);
        var store = TestGraph.Store([pkg, .. modules], []);
        using var tool = new PackageSummaryTool(store);

        var resp = await tool.ExecuteTypedForTest(
            new PackageSummaryRequest { Fqn = "root::a", IncludePublicExports = false, IncludeDependencies = false },
            CancellationToken.None);

        Assert.Equal(["/src/a/Program.cs", "/src/a/__main__.py", "/src/a/main.go"], resp.EntryPoints.Order(StringComparer.Ordinal));
    }
}
