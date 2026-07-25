using Jint;
using Jint.Runtime;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Tests.Bindings;

/// <summary>
/// First-class <c>rag.*</c> scripting namespace (RAG-03/C3, plan §8.4):
/// <c>rag.ingest</c> / <c>rag.query</c> route to the RAG subsystem pipelines,
/// options map onto the typed requests, globs expand through the VFS, the
/// <c>profile</c> option is a documented no-op, and calls without a wired
/// subsystem fail loudly with an actionable message.
/// </summary>
public sealed class RagNamespaceBindingTests
{
    private static readonly string[] TwoWorkspaceSources = ["/workspace/a.md", "/workspace/b.md"];
    private static readonly string[] ExpectedGlobExpansion = ["/workspace/docs/a.md", "/workspace/docs/sub/b.md"];

    private static Engine CreateEngine(
        FakeIngestionPipeline? ingest = null,
        FakeRagPipeline? query = null,
        Orkeon.Domain.FileSystem.IFileSystemService? fileSystem = null)
    {
        // The backend is all-or-nothing (AddOrkeonRag registers both pipelines
        // together); tests exercising a single surface still supply both fakes.
        var backend = ingest is null && query is null
            ? null
            : new Orkeon.Scripting.Bindings.RagScriptingBackend
            {
                IngestionPipeline = ingest ?? new FakeIngestionPipeline(),
                RagPipeline = query ?? new FakeRagPipeline(),
                FileSystem = fileSystem ?? new FakeFileSystemService(),
            };
        return new JsEngineFactory(ragBackend: backend).Create();
    }

    [Fact]
    public async Task Ingest_MapsOptions_OntoTheIngestionRequest_AndReturnsCounters()
    {
        var pipeline = new FakeIngestionPipeline
        {
            Report = new IngestionReport
            {
                Collection = "unset",
                SourcesAdded = 2,
                SourcesUnchanged = 1,
                ChunksEmbedded = 9,
                Duration = TimeSpan.FromSeconds(2),
            },
        };
        using var engine = CreateEngine(ingest: pipeline);

        var result = await Task.Run(() => engine.Evaluate("""
            rag.ingest({
                collection: 'docs',
                sources: ['/workspace/a.md', '/workspace/b.md'],
                chunkingStrategy: 'sentence',
                reindex: true,
            }).then(r => r.collection + ':' + r.sourcesAdded + ':' + r.sourcesUnchanged + ':' + r.chunksEmbedded)
            """).UnwrapIfPromise());

        Assert.Equal("docs:2:1:9", result.AsString());
        Assert.Equal("docs", pipeline.LastRequest?.Collection);
        Assert.Equal(TwoWorkspaceSources, pipeline.LastRequest!.Sources.Select(s => s.Location));
        Assert.Equal("sentence", pipeline.LastRequest.ChunkingStrategy);
        Assert.True(pipeline.LastRequest.Reindex);
    }

    [Fact]
    public async Task Ingest_AcceptsASingleSourceString_AndDefaults()
    {
        var pipeline = new FakeIngestionPipeline();
        using var engine = CreateEngine(ingest: pipeline);

        await Task.Run(() => engine.Evaluate(
            "rag.ingest({ collection: 'docs', sources: '/workspace/a.md' })").UnwrapIfPromise());

        var source = Assert.Single(pipeline.LastRequest!.Sources);
        Assert.Equal("/workspace/a.md", source.Location);
        Assert.Null(pipeline.LastRequest.ChunkingStrategy);
        Assert.False(pipeline.LastRequest.Reindex);
    }

    [Fact]
    public async Task Ingest_ExpandsGlobs_ThroughTheVirtualFileSystem()
    {
        var fs = new FakeFileSystemService()
            .AddMount("/workspace")
            .AddFile("/workspace/docs/a.md", "alpha")
            .AddFile("/workspace/docs/sub/b.md", "beta")
            .AddFile("/workspace/docs/skip.txt", "no");
        var pipeline = new FakeIngestionPipeline();
        using var engine = CreateEngine(ingest: pipeline, fileSystem: fs);

        await Task.Run(() => engine.Evaluate(
            "rag.ingest({ collection: 'docs', sources: ['/workspace/docs/**/*.md'] })").UnwrapIfPromise());

        Assert.Equal(ExpectedGlobExpansion, pipeline.LastRequest!.Sources.Select(s => s.Location));
    }

    [Fact]
    public async Task Query_MapsQuestion_Collection_AndTopN_AndReturnsCitations()
    {
        var pipeline = new FakeRagPipeline
        {
            Answer = new RagAnswer
            {
                Text = "Refunds within 30 days [1].",
                Citations =
                [
                    new Citation
                    {
                        Marker = 1,
                        ChunkId = "chunk-1",
                        SourceId = "faq.md",
                        Snippet = "30 days",
                        Score = 0.87,
                    },
                ],
            },
        };
        using var engine = CreateEngine(query: pipeline);

        var result = await Task.Run(() => engine.Evaluate("""
            rag.query('refund policy?', { collection: 'docs', topN: 7 })
               .then(r => r.text + '|' + r.citations.length + '|' + r.citations[0].sourceId + '|' + r.citations[0].marker)
            """).UnwrapIfPromise());

        Assert.Equal("Refunds within 30 days [1].|1|faq.md|1", result.AsString());
        Assert.Equal("refund policy?", pipeline.LastQuery?.Text);
        Assert.Equal("docs", pipeline.LastQuery?.Collection);
        Assert.Equal(7, pipeline.LastQuery?.TopN);
    }

    [Fact]
    public async Task Query_Profile_IsAccepted_ButIsANoOp_UntilRag04()
    {
        var pipeline = new FakeRagPipeline();
        using var engine = CreateEngine(query: pipeline);

        var result = await Task.Run(() => engine.Evaluate(
            "rag.query('q?', { collection: 'docs', profile: 'quality' }).then(r => r.text)").UnwrapIfPromise());

        Assert.Equal("fake answer", result.AsString());
        // Default TopN untouched — profile carried no retrieval tuning yet.
        Assert.Equal(5, pipeline.LastQuery?.TopN);
    }

    [Fact]
    public async Task Ingest_WithoutASubsystem_FailsLoudly_WithAnActionableMessage()
    {
        using var engine = CreateEngine();

        var ex = await Record.ExceptionAsync(() => Task.Run(() => engine.Evaluate(
            "rag.ingest({ collection: 'docs', sources: ['/a.md'] })").UnwrapIfPromise()));

        Assert.NotNull(ex);
        Assert.Contains("AddOrkeonRag", FlattenMessage(ex!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_WithoutASubsystem_FailsLoudly_WithAnActionableMessage()
    {
        using var engine = CreateEngine();

        var ex = await Record.ExceptionAsync(() => Task.Run(() => engine.Evaluate(
            "rag.query('q?', { collection: 'docs' })").UnwrapIfPromise()));

        Assert.NotNull(ex);
        Assert.Contains("AddOrkeonRag", FlattenMessage(ex!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ingest_WithoutACollection_IsRejected()
    {
        using var engine = CreateEngine(ingest: new FakeIngestionPipeline());

        var ex = await Record.ExceptionAsync(() => Task.Run(() => engine.Evaluate(
            "rag.ingest({ sources: ['/a.md'] })").UnwrapIfPromise()));

        Assert.NotNull(ex);
        Assert.Contains("collection", FlattenMessage(ex!), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Jint surfaces CLR exceptions wrapped (PromiseRejected/JavaScriptException); flatten the chain for assertions.</summary>
    private static string FlattenMessage(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        if (ex is PromiseRejectedException rejected)
            messages.Add(rejected.RejectedValue.ToString());
        return string.Join(" | ", messages);
    }
}
