using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Tests.Doubles;

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
    public async Task Respects_a_nested_gitignore_under_its_own_folder_only()
    {
        // Arrange — lib/.gitignore ignores generated/: under lib only, never the root-level generated/
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("lib/.gitignore", "generated/\n"),
            ("lib/generated/a.ts", "export const a = 1;"),
            ("lib/keep.ts", "export const k = 1;"),
            ("generated/b.ts", "export const b = 2;"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));

            // Act
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            // Assert
            Assert.Equal(["generated/b.ts", "lib/keep.ts"], result.Files.Select(f => f.RelativePath).Order().ToArray());
            Assert.Equal(1, result.FilterStats.ExcludedByGitignore);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Respects_git_info_exclude()
    {
        // Arrange — /packages is named in .git/info/exclude alone, as a package cache beside the sources is
        var dir = await TestFixtures.WriteDirectoryAsync([
            (".git/info/exclude", "/packages\n"),
            ("packages/vendor.ts", "export const v = 1;"),
            ("keep.ts", "export const k = 1;"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));

            // Act
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            // Assert
            Assert.Equal(["keep.ts"], result.Files.Select(f => f.RelativePath).ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Lets_a_nested_gitignore_re_include_what_the_root_ignores()
    {
        // Arrange — the root ignores *.gen.ts, lib/.gitignore takes keep.gen.ts back
        var dir = await TestFixtures.WriteDirectoryAsync([
            (".gitignore", "*.gen.ts\n"),
            ("lib/.gitignore", "!keep.gen.ts\n"),
            ("lib/keep.gen.ts", "export const k = 1;"),
            ("lib/other.gen.ts", "export const o = 1;"),
            ("top.gen.ts", "export const t = 1;"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));

            // Act
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            // Assert
            Assert.Equal(["lib/keep.gen.ts"], result.Files.Select(f => f.RelativePath).ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Keeps_a_file_under_an_ignored_folder_out_whatever_a_nested_gitignore_says()
    {
        // Arrange — git never re-includes a file whose folder is ignored
        var dir = await TestFixtures.WriteDirectoryAsync([
            (".gitignore", "vendor/\n"),
            ("vendor/.gitignore", "!*.ts\n"),
            ("vendor/lib.ts", "export const v = 1;"),
            ("keep.ts", "export const k = 1;"),
        ]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));

            // Act
            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            // Assert
            Assert.Equal(["keep.ts"], result.Files.Select(f => f.RelativePath).ToArray());
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
    public async Task ShouldReuseTheKnownHash_WhenSizeAndTimeAreUnchanged()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("a.ts", "const x = 1;")]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var first = (await sut.DiscoverAllAsync(new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None)).Files[0];
            // A hash the content cannot give: only a reuse without reading the file returns it.
            var known = new Dictionary<string, FileStamp> { [first.RelativePath] = new("known-sha", first.SizeBytes, first.LastModified) };

            var second = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot, KnownFiles = known }, CancellationToken.None);

            Assert.Equal("known-sha", second.Files[0].Sha256);
            Assert.Equal(first.LastModified, second.Files[0].LastModified);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("time")]
    [InlineData("size")]
    [InlineData("untrusted")]
    public async Task ShouldRehashTheFile_WhenItsStampDiffers(string differs)
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("a.ts", "const x = 1;")]);
        try
        {
            var sut = new FileSystemDiscoverer(TestFixtures.CreateFs(dir));
            var first = (await sut.DiscoverAllAsync(new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None)).Files[0];
            var stamp = differs switch
            {
                "time" => new FileStamp("known-sha", first.SizeBytes, first.LastModified.AddSeconds(-10)),
                "size" => new FileStamp("known-sha", first.SizeBytes + 1, first.LastModified),
                _ => new FileStamp("known-sha", first.SizeBytes, null),
            };

            var second = await sut.DiscoverAllAsync(
                new DiscoveryRequest
                {
                    RootPath = TestFixtures.TestVirtualRoot,
                    KnownFiles = new Dictionary<string, FileStamp> { [first.RelativePath] = stamp },
                },
                CancellationToken.None);

            Assert.Equal(first.Sha256, second.Files[0].Sha256);
        }
        finally { Directory.Delete(dir, recursive: true); }
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

    // Issue #215: a file the VFS refuses is skipped and counted; it never fails the discovery.
    [Fact]
    public async Task ShouldSkipAndCountDeniedFile_WhenTheVfsRefusesItsRead()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/run.sh", "#!/usr/bin/env bash\necho hi\n"),
            ("src/math.js", "export function add(a, b) { return a + b; }"),
        ]);
        try
        {
            var fs = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir))
            {
                DeniedReadPaths = { $"{TestFixtures.TestVirtualRoot}/src/run.sh" },
            };
            var sut = new FileSystemDiscoverer(fs);

            var result = await sut.DiscoverAllAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            var file = Assert.Single(result.Files);
            Assert.Equal("src/math.js", file.RelativePath);
            Assert.Equal(1, result.FilterStats.ExcludedByPolicy);
            Assert.Equal([$"{TestFixtures.TestVirtualRoot}/src/run.sh"], result.FilterStats.ExcludedByPolicyPaths.ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldStreamTheOtherFiles_WhenTheVfsRefusesOneRead()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/run.sh", "#!/usr/bin/env bash\necho hi\n"),
            ("src/math.js", "export function add(a, b) { return a + b; }"),
        ]);
        try
        {
            var fs = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir))
            {
                DeniedReadPaths = { $"{TestFixtures.TestVirtualRoot}/src/run.sh" },
            };
            var sut = new FileSystemDiscoverer(fs);

            var files = new List<DiscoveredFile>();
            await foreach (var f in sut.DiscoverAsync(
                new DiscoveryRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None))
                files.Add(f);

            Assert.Equal("src/math.js", Assert.Single(files).RelativePath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
