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

    [Theory]
    [InlineData("pom.xml", "maven")]
    [InlineData("build.gradle", "gradle")]
    [InlineData("build.gradle.kts", "gradle")]
    [InlineData("composer.json", "composer")]
    [InlineData("Gemfile", "bundler")]
    [InlineData("CMakeLists.txt", "cmake")]
    public async Task Detects_package_marker_of_the_languages_it_indexes(string markerFile, string marker)
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ($"lib/{markerFile}", ""),
            ("lib/src/a.c", "int a(void) { return 1; }"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            var package = Assert.Single(result.Packages);
            Assert.Equal(marker, package.MarkerFile);
            Assert.Equal("lib", package.RelativePath);
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

    [Fact]
    [Trait("BR", "RT-29")]
    public async Task ShouldDiscoverMarkdownFiles_WhenLanguagesFilterIsMarkdown()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.md", "# A"),
            ("b.markdown", "# B"),
            ("c.cs", "class C {}"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot, Languages = ["markdown"] },
                CancellationToken.None);

            Assert.Equal(
                ["a.md", "b.markdown"],
                result.Files.Select(f => f.RelativePath).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.All(result.Files, f => Assert.Equal("markdown", f.Language));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
