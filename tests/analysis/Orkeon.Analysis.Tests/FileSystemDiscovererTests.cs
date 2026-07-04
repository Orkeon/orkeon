using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

public class FileSystemDiscovererTests
{
    private static readonly string[] ExpectedLanguages = ["csharp", "go", "python", "rust", "typescript"];

    [Fact]
    public async Task Discovers_three_ts_files_and_excludes_node_modules()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
            ("b.ts", "export const b = 2;"),
            ("sub/c.ts", "export const c = 3;"),
            ("node_modules/d.ts", "export const d = 4;"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            Assert.Equal(3, result.Files.Length);
            Assert.All(result.Files, f => Assert.Equal("typescript", f.Language));
            Assert.DoesNotContain(result.Files, f => f.RelativePath.Contains("node_modules"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Respects_gitignore()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            (".gitignore", TestFixtures.Gitignore),
            ("keep.ts", "export const x = 1;"),
            ("dist/generated.ts", "export const y = 2;"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            Assert.Single(result.Files);
            Assert.Equal("keep.ts", result.Files[0].RelativePath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Detects_npm_package_marker()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/index.ts", "export {};"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            Assert.Single(result.Packages);
            Assert.Equal("npm", result.Packages[0].MarkerFile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Computes_sha256_deterministically()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "const x = 1;"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var r1 = await sut.DiscoverAllAsync(new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            var r2 = await sut.DiscoverAllAsync(new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            Assert.Equal(r1.Files[0].Sha256, r2.Files[0].Sha256);
            Assert.Equal(64, r1.Files[0].Sha256.Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Deduces_language_by_extension()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "const x = 1;"),
            ("b.py", "x = 1"),
            ("c.cs", "class C {}"),
            ("d.go", "package main"),
            ("e.rs", "fn main() {}"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var result = await sut.DiscoverAllAsync(new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            var languages = result.Files.Select(f => f.Language).OrderBy(x => x).ToArray();
            Assert.Equal(ExpectedLanguages, languages);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
