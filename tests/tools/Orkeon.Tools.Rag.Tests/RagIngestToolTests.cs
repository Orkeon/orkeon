using System.Text.Json;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Rag.Tests.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// Behavior of <see cref="RagIngestTool"/> (<c>rag_ingest</c>, RAG-03/C3): schema,
/// parameter tolerance (list / single string / JSON array sources), glob expansion
/// through the VFS, propagation to <c>IIngestionPipeline</c>, and the formatted
/// counter report.
/// </summary>
public class RagIngestToolTests
{
    private static readonly string[] TwoWorkspaceSources = ["/workspace/a.md", "/workspace/b.md"];
    private static readonly string[] ExpectedGlobExpansion = ["/workspace/docs/a.md", "/workspace/docs/sub/b.md"];

    private static RagIngestTool CreateTool(FakeIngestionPipeline pipeline, FakeFileSystemService? fs = null)
        => new(pipeline, fs ?? new FakeFileSystemService());

    private static IngestionReport SampleReport() => new()
    {
        Collection = "unset",
        DocumentsLoaded = 2,
        ChunksCreated = 8,
        ChunksEmbedded = 8,
        ChunksSkipped = 0,
        SourcesAdded = 2,
        SourcesUnchanged = 1,
        SourcesReingested = 0,
        Duration = TimeSpan.FromMilliseconds(1234),
    };

    [Fact]
    public void Tool_IsNamed_RagIngest_WithExpectedSchema()
    {
        var tool = CreateTool(new FakeIngestionPipeline());

        Assert.Equal("rag_ingest", tool.Name);
        Assert.True(tool.Schema.Parameters["collection"].Required);
        Assert.True(tool.Schema.Parameters["sources"].Required);
        Assert.False(tool.Schema.Parameters["chunking_strategy"].Required);
        Assert.False(tool.Schema.Parameters["reindex"].Required);
    }

    [Fact]
    public async Task CallAsync_Fails_WhenCollectionIsMissing()
    {
        var tool = CreateTool(new FakeIngestionPipeline());

        var response = await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?> { ["sources"] = new List<object?> { "/docs/a.md" } }),
            TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("collection", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_Fails_WhenSourcesAreMissing()
    {
        var tool = CreateTool(new FakeIngestionPipeline());

        var response = await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?> { ["collection"] = "docs" }),
            TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("sources", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_PropagatesCollection_Sources_Chunking_AndReindex()
    {
        var pipeline = new FakeIngestionPipeline { Report = SampleReport() };
        var tool = CreateTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?>
            {
                ["collection"] = "docs",
                ["sources"] = new List<object?> { "/workspace/a.md", "/workspace/b.md" },
                ["chunking_strategy"] = "sentence",
                ["reindex"] = true,
            }), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.Equal("docs", pipeline.LastRequest?.Collection);
        Assert.Equal(TwoWorkspaceSources, pipeline.LastRequest!.Sources.Select(s => s.Location));
        Assert.Equal("sentence", pipeline.LastRequest.ChunkingStrategy);
        Assert.True(pipeline.LastRequest.Reindex);
    }

    [Fact]
    public async Task CallAsync_Defaults_NoChunkingStrategy_NoReindex()
    {
        var pipeline = new FakeIngestionPipeline { Report = SampleReport() };
        var tool = CreateTool(pipeline);

        await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?>
            {
                ["collection"] = "docs",
                ["sources"] = "/workspace/a.md", // single string tolerated
            }), TestContext.Current.CancellationToken);

        Assert.Null(pipeline.LastRequest?.ChunkingStrategy);
        Assert.False(pipeline.LastRequest!.Reindex);
        var source = Assert.Single(pipeline.LastRequest.Sources);
        Assert.Equal("/workspace/a.md", source.Location);
    }

    [Fact]
    public async Task CallAsync_AcceptsJsonElementArraySources_AndBooleanStrings()
    {
        // The structured tool-calling protocol hands arrays over as JsonElement.
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>("""
            { "collection": "docs", "sources": ["/w/a.md", "/w/b.md"], "reindex": "true" }
            """)!;
        var pipeline = new FakeIngestionPipeline { Report = SampleReport() };
        var tool = CreateTool(pipeline);

        var response = await tool.CallAsync(
            new ToolCallRequest("rag_ingest", parameters), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.Equal(2, pipeline.LastRequest!.Sources.Count);
        Assert.True(pipeline.LastRequest.Reindex);
    }

    [Fact]
    public async Task CallAsync_ExpandsGlobs_ThroughTheVirtualFileSystem()
    {
        var fs = new FakeFileSystemService()
            .AddMount("/workspace")
            .AddFile("/workspace/docs/a.md", "alpha")
            .AddFile("/workspace/docs/sub/b.md", "beta")
            .AddFile("/workspace/docs/ignore.txt", "nope");
        var pipeline = new FakeIngestionPipeline { Report = SampleReport() };
        var tool = CreateTool(pipeline, fs);

        var response = await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?>
            {
                ["collection"] = "docs",
                ["sources"] = new List<object?> { "/workspace/docs/**/*.md" },
            }), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.Equal(ExpectedGlobExpansion, pipeline.LastRequest!.Sources.Select(s => s.Location));
    }

    [Fact]
    public async Task CallAsync_Fails_WhenAGlobMatchesNothing()
    {
        var fs = new FakeFileSystemService().AddMount("/workspace");
        var tool = CreateTool(new FakeIngestionPipeline(), fs);

        var response = await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?>
            {
                ["collection"] = "docs",
                ["sources"] = new List<object?> { "/workspace/**/*.pdf" },
            }), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("no source matched", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_FormatsTheCounterReport()
    {
        var pipeline = new FakeIngestionPipeline { Report = SampleReport() };
        var tool = CreateTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?>
            {
                ["collection"] = "docs",
                ["sources"] = new List<object?> { "/workspace/a.md" },
            }), TestContext.Current.CancellationToken);

        var text = Assert.IsType<string>(response.Result);
        Assert.Contains("Ingestion report for collection 'docs':", text, StringComparison.Ordinal);
        Assert.Contains("2 added, 1 unchanged, 0 re-ingested", text, StringComparison.Ordinal);
        Assert.Contains("Documents loaded: 2", text, StringComparison.Ordinal);
        Assert.Contains("8 created, 8 embedded, 0 skipped", text, StringComparison.Ordinal);
        Assert.Contains("Duration: 1.23s", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Errors:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallAsync_AppendsErrorsBlock_WhenTheRunWasNotClean()
    {
        var pipeline = new FakeIngestionPipeline
        {
            Report = SampleReport() with { Errors = ["boom on /w/a.md"] },
        };
        var tool = CreateTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_ingest",
            new Dictionary<string, object?>
            {
                ["collection"] = "docs",
                ["sources"] = new List<object?> { "/workspace/a.md" },
            }), TestContext.Current.CancellationToken);

        var text = Assert.IsType<string>(response.Result);
        Assert.Contains("Errors:", text, StringComparison.Ordinal);
        Assert.Contains("- boom on /w/a.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_LegacyStringInput_IsRejected_WithGuidance()
    {
        var tool = CreateTool(new FakeIngestionPipeline());

        var result = await tool.ExecuteAsync("docs", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("structured parameters", result.Error, StringComparison.Ordinal);
        Assert.False(tool.ValidateInput("docs"));
    }
}
