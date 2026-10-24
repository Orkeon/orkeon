using System.Diagnostics;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Tests.Doubles;

namespace Orkeon.Analysis.Tests;

public class IncrementalReindexBoundTests
{
    private const string BigPath = "docs/big.md";

    [Fact]
    [Trait("BR", "RT-30")]
    public async Task ShouldReadAndParseOneFileOnly_WhenTwoThousandLineFileReindexedInIndexedRepository()
    {
        var (dir, tree) = await IndexRepositoryAsync();
        try
        {
            var counting = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir));
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), counting);
            var big = $"{TestFixtures.TestVirtualRoot}/{BigPath}";

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [big],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var sourceReads = counting.Reads
                .Where(p => p.EndsWith(".md", StringComparison.Ordinal) || p.EndsWith(".ts", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal([big], sourceReads);
            Assert.Equal(1, result.FilesParsed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "RT-30")]
    [Trait("Category", "Slow")]
    public async Task ShouldReindexTwoThousandLineFileWithinBound_WhenRepositoryIndexed()
    {
        var (dir, tree) = await IndexRepositoryAsync();
        try
        {
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));
            var stopwatch = Stopwatch.StartNew();

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/{BigPath}"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"reindex took {stopwatch.Elapsed}");
            Assert.True(result.Elapsed > TimeSpan.Zero, "engine reported no elapsed time");
            Assert.True(result.Elapsed <TimeSpan.FromSeconds(2), $"engine reported {result.Elapsed}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static async Task<(string Dir, RaggableTree Tree)> IndexRepositoryAsync()
    {
        var big = string.Join('\n', Enumerable.Range(0, 1000).Select(i => $"## Section {i}\ntext {i}"));
        var dir = await TestFixtures.WriteDirectoryAsync([
            (BigPath, big),
            ("docs/a.md", "# A\ntext"),
            ("docs/b.md", "# B\ntext"),
            ("src/a.ts", "export class A { run(): number { return 1; } }"),
            ("src/b.ts", "export class B { run(): number { return 2; } }"),
        ]);
        var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));
        var built = await builder.BuildAsync(
            TestFixtures.TestVirtualRoot,
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
            CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(dir, "docs", "big.md"), big + "\n## Added\ntext", CancellationToken.None);
        return (dir, built.Tree);
    }
}
