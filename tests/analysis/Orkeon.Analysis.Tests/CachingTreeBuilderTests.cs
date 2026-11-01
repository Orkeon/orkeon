using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Core.Cache;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Tests;

public class CachingTreeBuilderTests
{
    private static readonly IndexCodebaseRequest Request = new() { RootPath = TestFixtures.TestVirtualRoot };

    private static async Task<string> FixtureAsync() => await TestFixtures.WriteDirectoryAsync([
        ("package.json", """{ "name": "app" }"""),
        ("src/a.ts", "import { b } from './b';\nexport function a() { return b(); }\n"),
        ("src/b.ts", "export function b() { return 1; }\n"),
        ("src/c.ts", "import { a } from './a';\nexport function c() { return a(); }\n"),
        ("src/d.ts", "export class D { run() { return 2; } }\n"),
        ("docs/guide.md", "# Guide\n\nCall `b` first.\n"),
    ]);

    [Fact]
    public async Task ShouldReloadTheSavedIndex_WhenNoFileChanged()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var first = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            var second = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Miss, first.Outcome);
            Assert.Equal(IndexCacheOutcome.Hit, second.Outcome);
            Assert.Equal(first.Build.IndexId, second.Build.IndexId);
            Assert.Equal(first.Build.FileCount, second.Build.FileCount);
            Assert.Equal(first.Build.Tree.Nodes.Select(n => n.Id), second.Build.Tree.Nodes.Select(n => n.Id));
            Assert.Equal(first.Build.Tree.Edges.Select(e => e.Id), second.Build.Tree.Edges.Select(e => e.Id));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldReindexOnlyTheChangedFiles_WhenAFewChanged()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "d.ts"),
                "export class D { run() { return 2; } stop() { return 3; } }\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "e.ts"),
                "export function e() { return 5; }\n", TestContext.Current.CancellationToken);

            var refreshed = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            var fresh = await new RaggableTreeBuilder(new TypeScriptAdapter(), fs)
                .BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Refreshed, refreshed.Outcome);
            Assert.Equal(2, refreshed.ReindexedFileCount);
            Assert.Equal(fresh.IndexId, refreshed.Build.IndexId);
            Assert.Equal(fresh.Tree.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal),
                refreshed.Build.Tree.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal));
            Assert.Equal(fresh.Tree.Edges.Select(e => e.Id).Order(StringComparer.Ordinal),
                refreshed.Build.Tree.Edges.Select(e => e.Id).Order(StringComparer.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldDropTheNodesOfARemovedFile_WhenRefreshing()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            File.Delete(Path.Combine(dir, "src", "d.ts"));

            var refreshed = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Refreshed, refreshed.Outcome);
            Assert.Equal(1, refreshed.ReindexedFileCount);
            Assert.DoesNotContain(refreshed.Build.Tree.Nodes, n => n.VirtualFilePath.EndsWith("/d.ts", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldSaveTheRefreshedIndex_SoTheNextCallIsAHit()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "b.ts"), "export function b() { return 7; }\n", TestContext.Current.CancellationToken);
            var refreshed = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            var next = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Hit, next.Outcome);
            Assert.Equal(refreshed.Build.IndexId, next.Build.IndexId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldBuildFromScratch_WhenMoreThanHalfOfTheFilesChanged()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            foreach (var name in new[] { "a", "b", "c", "d" })
            {
                await File.AppendAllTextAsync(Path.Combine(dir, "src", $"{name}.ts"), "// changed\n", TestContext.Current.CancellationToken);
            }

            var result = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Miss, result.Outcome);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldBuildFromScratch_WhenAPackageMarkerChanged()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(dir, "package.json"), """{ "name": "app2" }""", TestContext.Current.CancellationToken);

            var result = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Miss, result.Outcome);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldBuildFromScratch_WhenAnOptionShapingTheTreeChanged()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            var aliased = await Caching(fs).BuildAsync(
                TestFixtures.TestVirtualRoot, Request with { RootAlias = "app" }, CancellationToken.None);
            var parallel = await Caching(fs).BuildAsync(
                TestFixtures.TestVirtualRoot, Request with { RootAlias = "app", MaxParallelism = 1 }, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Miss, aliased.Outcome);
            Assert.Equal(IndexCacheOutcome.Hit, parallel.Outcome);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldBuildFromScratch_WhenTheEnvironmentChanged()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs, environment: "embedder:1536").BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            var result = await Caching(fs, environment: "embedder:384").BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Miss, result.Outcome);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldRebuildAndSave_WhenUseCacheIsFalse()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            await File.AppendAllTextAsync(Path.Combine(dir, "src", "b.ts"), "// changed\n", TestContext.Current.CancellationToken);

            var bypassed = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request with { UseCache = false }, CancellationToken.None);
            var next = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Bypassed, bypassed.Outcome);
            Assert.Equal(IndexCacheOutcome.Hit, next.Outcome);
            Assert.Equal(bypassed.Build.IndexId, next.Build.IndexId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldStillBuild_WhenTheCacheCannotBeWritten()
    {
        var dir = await FixtureAsync();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, ".orkeon"), "a file, not a folder", TestContext.Current.CancellationToken);
            var fs = TestFixtures.CreateFs(dir);

            var first = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            var second = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Miss, first.Outcome);
            Assert.Equal(IndexCacheOutcome.Miss, second.Outcome);
            Assert.NotEmpty(second.Build.Tree.Nodes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldReloadTheSavedIndex_WhenStatementsNestDeeperThanTheSharedJsonLimit()
    {
        var body = string.Concat(Enumerable.Range(0, 40).Select(i => $"if (x > {i}) {{ ")) + "return x;" + new string('}', 40);
        var dir = await TestFixtures.WriteDirectoryAsync([("deep.ts", $"export function deep(x: number) {{ {body} return 0; }}\n")]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var first = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            var second = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Hit, second.Outcome);
            Assert.Equal(
                first.Build.Tree.Nodes.Sum(n => n.Statements.Count),
                second.Build.Tree.Nodes.Sum(n => n.Statements.Count));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldSeeAChangeOfTheSameSize_WhenTheFileWasWrittenJustBeforeTheSave()
    {
        var dir = await FixtureAsync();
        try
        {
            // A time inside the racy window whatever the load of the machine: the save does not trust it.
            var recent = DateTime.UtcNow.AddHours(1);
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, recent);
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            var path = Path.Combine(dir, "src", "b.ts");
            await File.WriteAllTextAsync(path, "export function b() { return 7; }\n", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, recent);

            var refreshed = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Refreshed, refreshed.Outcome);
            Assert.Equal(1, refreshed.ReindexedFileCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldTrustTheSavedStamp_WhenTheFileIsOlderThanTheRacyWindow()
    {
        var dir = await FixtureAsync();
        try
        {
            var old = DateTime.UtcNow.AddHours(-1);
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, old);
            var fs = TestFixtures.CreateFs(dir);
            await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            // Same size, same time: like git, the change is not seen without use_cache false.
            var path = Path.Combine(dir, "src", "b.ts");
            await File.WriteAllTextAsync(path, "export function b() { return 7; }\n", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, old);

            var second = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            var bypassed = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request with { UseCache = false }, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Hit, second.Outcome);
            Assert.NotEqual(second.Build.IndexId, bypassed.Build.IndexId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R-04")]
    public async Task ShouldDropNewlyIgnoredFiles_WhenGitignoreChangesBetweenBuilds()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var first = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);
            Assert.Contains(first.Build.Tree.Nodes, n => n.VirtualFilePath.EndsWith("/src/d.ts", StringComparison.Ordinal));
            await File.WriteAllTextAsync(Path.Combine(dir, ".gitignore"), "src/d.ts\n", TestContext.Current.CancellationToken);

            var second = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.DoesNotContain(second.Build.Tree.Nodes, n => n.VirtualFilePath.EndsWith("/src/d.ts", StringComparison.Ordinal));
            Assert.Contains(second.Build.Tree.Nodes, n => n.VirtualFilePath.EndsWith("/src/a.ts", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ShouldDescribeTheEmbeddingSize_InTheEnvironment()
    {
        var adapters = new ILanguageAdapter[] { new TypeScriptAdapter() };

        var small = CachingTreeBuilder.DescribeEnvironment(adapters, [], null, new Doubles.StubEmbeddingProvider { Size = 384 });
        var large = CachingTreeBuilder.DescribeEnvironment(adapters, [], null, new Doubles.StubEmbeddingProvider { Size = 1536 });
        var none = CachingTreeBuilder.DescribeEnvironment(adapters, [], null, null);

        Assert.NotEqual(small, large);
        Assert.NotEqual(small, none);
        Assert.Contains("adapter:typescript:", none, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-37")]
    public async Task ShouldIndexWithoutModulePath_WhenGoModIsRefusedByFileSystem()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("go.mod", "module example.com/app\n\ngo 1.22\n"),
            ("src/a.ts", "export function a() { return 1; }\n"),
        ]);
        try
        {
            var fs = new Doubles.FakeCountingFileSystemService(TestFixtures.CreateFs(dir));
            fs.DeniedTextReadPaths.Add($"{TestFixtures.TestVirtualRoot}/go.mod");

            var built = await new RaggableTreeBuilder(new TypeScriptAdapter(), fs)
                .BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Contains(built.Tree.Nodes, n => n.VirtualFilePath.EndsWith("/src/a.ts", StringComparison.Ordinal));
            Assert.DoesNotContain(built.Tree.Nodes, n => n.Tags.ContainsKey(DependencyGraphBuilder.GoModuleTag));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R-37")]
    public async Task ShouldBuildTree_WhenPackageMarkerIsRefusedByFileSystem()
    {
        var dir = await FixtureAsync();
        try
        {
            var fs = new Doubles.FakeCountingFileSystemService(TestFixtures.CreateFs(dir));
            fs.DeniedTextReadPaths.Add($"{TestFixtures.TestVirtualRoot}/package.json");

            var result = await Caching(fs).BuildAsync(TestFixtures.TestVirtualRoot, Request, CancellationToken.None);

            Assert.Equal(IndexCacheOutcome.Miss, result.Outcome);
            Assert.Contains(result.Build.Tree.Nodes, n => n.VirtualFilePath.EndsWith("/src/a.ts", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static CachingTreeBuilder Caching(IFileSystemService fs, string environment = "test") => new(
        new RaggableTreeCache(fs),
        new FileSystemDiscoverer(fs),
        fs,
        () => new RaggableTreeBuilder(new TypeScriptAdapter(), fs),
        () => new IncrementalReindexEngine(new TypeScriptAdapter(), fs),
        environment);
}
