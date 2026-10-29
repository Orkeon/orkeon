using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

public class IndexCodebaseToolTests
{
    // The builder factory is only invoked for a valid root path. For the
    // contract paths under test (missing root_path), it must never be called,
    // so a throwing factory doubles as an assertion that the guard short-circuits.
    private static Func<RaggableTreeBuilder> ThrowingFactory =>
        () => throw new InvalidOperationException("builder must not be invoked for invalid input");

    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new IndexCodebaseTool(TestGraph.Store([], []), ThrowingFactory);
        Assert.Equal("index_codebase", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public async Task Returns_validation_error_when_root_path_is_empty()
    {
        using var tool = new IndexCodebaseTool(TestGraph.Store([], []), ThrowingFactory);

        var resp = await tool.ExecuteTypedForTest(
            new IndexCodebaseRequest { RootPath = "" },
            CancellationToken.None);

        Assert.Equal(string.Empty, resp.IndexId);
        Assert.Equal(0, resp.NodeCount);
        Assert.NotEmpty(resp.Errors);
        Assert.Contains(resp.Errors, e => e.Contains("root_path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Returns_validation_error_when_root_path_is_whitespace()
    {
        using var tool = new IndexCodebaseTool(TestGraph.Store([], []), ThrowingFactory);

        var resp = await tool.ExecuteTypedForTest(
            new IndexCodebaseRequest { RootPath = "   " },
            CancellationToken.None);

        Assert.NotEmpty(resp.Errors);
        Assert.Equal(TimeSpan.Zero, resp.Elapsed);
    }

    // GAP-15: include_statements and the summarizer_* settings were accepted and never read.
    // An agent that still sends them is not refused: JsonComponentSerializer ignores an
    // unknown field (see IndexCodebaseRequestToleranceTests in Orkeon.Infrastructure.Tests,
    // which holds the production serializer this project does not reference).
    [Fact]
    public void Request_exposes_only_the_settings_the_build_reads()
    {
        var names = typeof(IndexCodebaseRequest).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("IncludeStatements", names);
        Assert.DoesNotContain("SummarizerModel", names);
        Assert.DoesNotContain("SummarizerMaxTokens", names);
        Assert.DoesNotContain("SummarizerConcurrency", names);
    }

    [Fact]
    public async Task Returns_validation_error_when_max_parallelism_is_negative()
    {
        using var tool = new IndexCodebaseTool(TestGraph.Store([], []), ThrowingFactory);

        var resp = await tool.ExecuteTypedForTest(
            new IndexCodebaseRequest { RootPath = "/src", MaxParallelism = -2 },
            CancellationToken.None);

        Assert.Equal(0, resp.NodeCount);
        Assert.Contains(resp.Errors, e => e.Contains("max_parallelism", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Indexes_with_any_non_negative_max_parallelism(int maxParallelism)
    {
        var dir = Directory.CreateTempSubdirectory("index-parallel").FullName;
        try
        {
            for (var i = 0; i < 6; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, $"m{i}.ts"),
                    $"export function f{i}(): number {{ return {i}; }}\n", TestContext.Current.CancellationToken);
            }
            var fs = new DiskBackedFileSystemService(dir, "/src");
            using var tool = new IndexCodebaseTool(TestGraph.Store([], []), () => new RaggableTreeBuilder(new TypeScriptAdapter(), fs));

            var resp = await tool.ExecuteTypedForTest(
                new IndexCodebaseRequest { RootPath = "/src", MaxParallelism = maxParallelism },
                CancellationToken.None);

            Assert.Empty(resp.Errors);
            Assert.Equal(6, resp.FileCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
