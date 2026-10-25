using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.Fixtures.DevRepo;

namespace Orkeon.Analysis.Tests;

public sealed class AffectedTestsQueryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "affected-tests-" + Guid.NewGuid().ToString("N"));

    public AffectedTestsQueryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Lib(string cls, string method) => $"public class {cls} {{ public static void {method}() {{ }} }}\n";

    private static string Test(string cls, string call)
        => $"public class {cls}\n{{\n    [Fact]\n    public void Exercises()\n    {{\n        {call};\n    }}\n}}\n";

    private async Task<(InMemoryRaggableStore Store, RaggableTree Tree)> IndexAsync(DevRepoFixtureBuilder repo)
    {
        IFileSystemService fs = TestFixtures.CreateFs(_dir);
        repo.Build(fs, TestFixtures.TestVirtualRoot);
        var result = await new RaggableTreeBuilder(new CSharpAdapter(), fs).BuildAsync(
            TestFixtures.TestVirtualRoot,
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
            CancellationToken.None);
        return (new InMemoryRaggableStore(result.Tree, fs), result.Tree);
    }

    private static DevRepoFixtureBuilder ThreeLayers() => new DevRepoFixtureBuilder()
        .WithProject("A")
        .WithProject("B", "A")
        .WithProject("C")
        .WithTestProject("A.Tests", "A")
        .WithTestProject("B.Tests", "B")
        .WithTestProject("C.Tests", "C")
        .WithCSharpFile("A/Alpha.cs", Lib("Alpha", "Foo"))
        .WithCSharpFile("B/Beta.cs", Lib("Beta", "Bar"))
        .WithCSharpFile("C/Gamma.cs", Lib("Gamma", "Baz"))
        .WithCSharpFile("A.Tests/AlphaTests.cs", Test("AlphaTests", "Alpha.Foo()"))
        .WithCSharpFile("A.Tests/OtherTests.cs", Test("OtherTests", "var x = 1"))
        .WithCSharpFile("B.Tests/BetaTests.cs", Test("BetaTests", "Beta.Bar()"))
        .WithCSharpFile("C.Tests/GammaTests.cs", Test("GammaTests", "Gamma.Baz()"));

    [Fact]
    [Trait("BR", "RT-34")]
    public async Task ShouldReturnTestProjectsReached_WhenChangedFileBelongsToReferencedProject()
    {
        var (store, _) = await IndexAsync(ThreeLayers());

        var result = await AffectedTestsQuery.RunAsync(
            store, ["A/Alpha.cs"], TestFixtures.TestVirtualRoot, CancellationToken.None);

        Assert.False(result.All);
        Assert.Equal(["A.Tests", "B.Tests"], result.TestProjects.Order(StringComparer.Ordinal));
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    [Trait("BR", "RT-34")]
    public async Task ShouldReturnTestClassesThroughTestsEdges_WhenSymbolChanged()
    {
        var (store, tree) = await IndexAsync(ThreeLayers());
        var alphaTests = Assert.Single(tree.Nodes, n => n.Kind == UniversalNodeKind.Class && n.Name == "AlphaTests");

        var result = await AffectedTestsQuery.RunAsync(
            store, ["A/Alpha.cs"], TestFixtures.TestVirtualRoot, CancellationToken.None);

        Assert.Equal([alphaTests.Fqn], result.TestClasses);
    }

    [Fact]
    [Trait("BR", "RT-34")]
    public async Task ShouldReturnAll_WhenChangedFileHasNoOwningProject()
    {
        var (store, _) = await IndexAsync(ThreeLayers().WithCSharpFile("Loose.cs", Lib("Loose", "Run")));
        const string root = TestFixtures.TestVirtualRoot;

        var loose = await AffectedTestsQuery.RunAsync(store, ["Loose.cs"], root, CancellationToken.None);
        var props = await AffectedTestsQuery.RunAsync(store, ["Directory.Build.props"], root, CancellationToken.None);
        var neutral = await AffectedTestsQuery.RunAsync(store, ["docs/guide.md"], root, CancellationToken.None);

        Assert.True(loose.All);
        Assert.True(props.All);
        Assert.Equal(["Directory.Build.props"], props.Unresolved);
        Assert.False(neutral.All);
        Assert.Empty(neutral.TestProjects);
        Assert.Empty(neutral.TestClasses);
        Assert.Empty(neutral.Unresolved);
    }

    // The golden affected-tests.json was produced by running affected-tests.py (dev-implement/scripts)
    // offline on a throwaway git repo of the same projects and files; its `cases` are what the script printed.
    [Fact]
    [Trait("BR", "RT-34")]
    public async Task ShouldMatchAffectedTestsGolden_WhenRunOnFixtureRepo()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DevRepo", "Goldens", "affected-tests.json");
        using var golden = JsonDocument.Parse(await File.ReadAllTextAsync(goldenPath, TestContext.Current.CancellationToken));
        var root = golden.RootElement;

        var repo = new DevRepoFixtureBuilder();
        foreach (var p in root.GetProperty("projects").EnumerateArray())
        {
            var refs = p.GetProperty("references").EnumerateArray().Select(r => r.GetString()!).ToArray();
            var name = p.GetProperty("name").GetString()!;
            if (p.GetProperty("is_test").GetBoolean()) repo.WithTestProject(name, refs);
            else repo.WithProject(name, refs);
        }
        foreach (var f in root.GetProperty("files").EnumerateArray())
        {
            var path = f.GetProperty("path").GetString()!;
            var content = f.GetProperty("content").GetString()!;
            if (path.EndsWith(".md", StringComparison.Ordinal)) repo.WithMarkdownFile(path, content);
            else repo.WithCSharpFile(path, content);
        }
        var (store, _) = await IndexAsync(repo);

        var cases = root.GetProperty("cases").EnumerateArray().ToList();
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var changed = c.GetProperty("changed").EnumerateArray().Select(x => x.GetString()!).ToList();
            var result = await AffectedTestsQuery.RunAsync(store, changed, TestFixtures.TestVirtualRoot, CancellationToken.None);

            var label = string.Join(",", changed);
            Assert.True(c.GetProperty("all").GetBoolean() == result.All, $"all for {label}");
            if (result.All) continue;
            var expected = c.GetProperty("test_projects").EnumerateArray().Select(x => x.GetString()!).Order(StringComparer.Ordinal);
            Assert.Equal(expected, result.TestProjects.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    [Trait("BR", "R18")]
    public async Task ShouldReturnTsTest_WhenAffectedTestsQueriesAChangedTsSymbol()
    {
        IFileSystemService fs = TestFixtures.CreateFs(_dir);
        new DevRepoFixtureBuilder()
            .WithProject("P")
            .WithTestProject("P.Tests", "P")
            .WithTypeScriptFile("P/lib.ts", "export function helper() { return 1; }\n")
            .WithTypeScriptFile("P.Tests/lib.spec.ts", "import { helper } from '../P/lib';\ntest('x', () => { helper(); });\n")
            .Build(fs, TestFixtures.TestVirtualRoot);
        var built = await new RaggableTreeBuilder(new TypeScriptAdapter(), fs).BuildAsync(
            TestFixtures.TestVirtualRoot,
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
            CancellationToken.None);
        var store = new InMemoryRaggableStore(built.Tree, fs);
        var module = Assert.Single(built.Tree.Nodes, n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath == "/src/P.Tests/lib.spec.ts");

        var result = await AffectedTestsQuery.RunAsync(
            store, ["P/lib.ts"], TestFixtures.TestVirtualRoot, CancellationToken.None);

        Assert.Equal([module.Fqn], result.TestClasses);
    }

    [Theory]
    [InlineData("README.md", false)]
    [InlineData("notes/CHANGES.TXT", false)]
    [InlineData("assets/logo.png", false)]
    [InlineData("assets/photo.JPG", false)]
    [InlineData("assets/icon.svg", false)]
    [InlineData("LICENSE", false)]
    [InlineData("LICENSE.md", false)]
    [InlineData("sub/LICENSE-THIRD-PARTY", false)]
    [InlineData(".gitignore", false)]
    [InlineData(".gitattributes", false)]
    [InlineData(".editorconfig", false)]
    [InlineData(".claude/skills/x.cs", false)]
    [InlineData("src/Program.cs", true)]
    [InlineData("src/readme.markdown", true)]
    [InlineData("LICENSE/Header.cs", true)]
    [InlineData("build.props", true)]
    public async Task ShouldSkipNeutralFiles_WhenNothingButNeutralFilesChanged(string file, bool expectedAll)
    {
        var store = new StubUnorderedRaggableStore();

        var result = await AffectedTestsQuery.RunAsync(store, [file], TestFixtures.TestVirtualRoot, CancellationToken.None);

        Assert.Equal(expectedAll, result.All);
        Assert.Empty(result.TestProjects);
    }
}
