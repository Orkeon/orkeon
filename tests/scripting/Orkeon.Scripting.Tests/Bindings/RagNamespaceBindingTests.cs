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
/// <c>profile</c> option resolves a pipeline for that call, and calls without a
/// wired subsystem fail loudly with an actionable message.
/// </summary>
public sealed class RagNamespaceBindingTests
{
    private static readonly string[] TwoWorkspaceSources = ["/workspace/a.md", "/workspace/b.md"];
    private static readonly string[] ExpectedGlobExpansion = ["/workspace/docs/a.md", "/workspace/docs/sub/b.md"];

    private static Engine CreateEngine(
        FakeIngestionPipeline? ingest = null,
        Orkeon.Rag.Abstractions.Interfaces.IRagPipeline? query = null,
        Orkeon.Domain.FileSystem.IFileSystemService? fileSystem = null,
        Orkeon.Rag.Abstractions.Interfaces.IRagProfileResolver? profileResolver = null)
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
                ProfileResolver = profileResolver,
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

    // The per-call `profile` option used to be a documented no-op: it logged
    // "profile ignored" and served the host-wide pipeline. That predated the
    // profiles themselves (RAG-04/05/06); a script asking for `corrective` and
    // silently getting `fast` produced answers whose provenance it could not
    // describe. These three tests pin the replacement behaviour.
    [Fact]
    public async Task Query_Profile_ResolvesAPipelineForThatCall()
    {
        var hostWide = new FakeRagPipeline();
        var profiled = new FakeRagPipeline();
        var resolver = new RecordingProfileResolver(profiled);
        using var engine = CreateEngine(query: hostWide, profileResolver: resolver);

        var result = await Task.Run(() => engine.Evaluate(
            "rag.query('q?', { collection: 'docs', profile: 'quality' }).then(r => r.text)").UnwrapIfPromise());

        Assert.Equal("fake answer", result.AsString());
        Assert.Equal("quality", Assert.Single(resolver.ResolvedProfiles));
        // The profiled pipeline answered; the host-wide one was never asked.
        Assert.NotNull(profiled.LastQuery);
        Assert.Null(hostWide.LastQuery);
    }

    [Fact]
    public async Task Query_WithoutAProfile_UsesTheHostWidePipeline_AndNeverResolves()
    {
        var hostWide = new FakeRagPipeline();
        var resolver = new RecordingProfileResolver(new FakeRagPipeline());
        using var engine = CreateEngine(query: hostWide, profileResolver: resolver);

        await Task.Run(() => engine.Evaluate(
            "rag.query('q?', { collection: 'docs' }).then(r => r.text)").UnwrapIfPromise());

        Assert.NotNull(hostWide.LastQuery);
        Assert.Empty(resolver.ResolvedProfiles);
    }

    [Fact]
    public async Task Query_Profile_OnAHostWithoutAResolver_FailsRatherThanSilentlyServingTheDefault()
    {
        var hostWide = new FakeRagPipeline();
        using var engine = CreateEngine(query: hostWide, profileResolver: null);

        var ex = await Record.ExceptionAsync(() => Task.Run(() => engine.Evaluate(
            "rag.query('q?', { collection: 'docs', profile: 'corrective' })").UnwrapIfPromise()));

        Assert.NotNull(ex);
        var message = FlattenMessage(ex!);
        Assert.Contains("corrective", message, StringComparison.Ordinal);
        Assert.Contains("IRagProfileResolver", message, StringComparison.Ordinal);
        // Nothing was answered from the default pipeline behind the caller's back.
        Assert.Null(hostWide.LastQuery);
    }

    [Fact]
    public async Task Query_UnknownProfile_ListsTheValidNames()
    {
        var resolver = new ThrowingProfileResolver();
        using var engine = CreateEngine(query: new FakeRagPipeline(), profileResolver: resolver);

        var ex = await Record.ExceptionAsync(() => Task.Run(() => engine.Evaluate(
            "rag.query('q?', { collection: 'docs', profile: 'nope' })").UnwrapIfPromise()));

        Assert.NotNull(ex);
        var message = FlattenMessage(ex!);
        foreach (var name in new[] { "fast", "balanced", "quality", "corrective", "adaptive" })
            Assert.Contains(name, message, StringComparison.Ordinal);
    }

    /// <summary>Records what was asked for and always returns the same pipeline.</summary>
    private sealed class RecordingProfileResolver(Orkeon.Rag.Abstractions.Interfaces.IRagPipeline pipeline)
        : Orkeon.Rag.Abstractions.Interfaces.IRagProfileResolver
    {
        public List<string> ResolvedProfiles { get; } = [];

        public Orkeon.Rag.Abstractions.Interfaces.IRagPipeline Resolve(string profileName)
        {
            ResolvedProfiles.Add(profileName);
            return pipeline;
        }
    }

    /// <summary>Stands in for a resolver that does not know the requested name.</summary>
    private sealed class ThrowingProfileResolver : Orkeon.Rag.Abstractions.Interfaces.IRagProfileResolver
    {
        public Orkeon.Rag.Abstractions.Interfaces.IRagPipeline Resolve(string profileName)
            => throw new ArgumentException($"unknown profile '{profileName}'", nameof(profileName));
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

    // ── rag.retrieve — retrieval without the generation stage ──────────────

    [Fact]
    public async Task Retrieve_TakesTheRetrievalSurface_AndNeverGenerates()
    {
        // The whole point of the surface. A `retrieve` that quietly called
        // QueryAsync would charge the caller for the stage it asked to skip —
        // exp02's round-41 paid 14 748 completion tokens that way.
        var pipeline = new FakeRetrievalCapableRagPipeline
        {
            Retrieved = new RagAnswer
            {
                Text = "",
                Citations = [new Citation { Marker = 1, ChunkId = "c1", SourceId = "/kb/a.md", Snippet = "PASSAGE", Score = 0.9 }],
            },
        };
        using var engine = CreateEngine(query: pipeline);

        var result = await Task.Run(() => engine.Evaluate("""
            rag.retrieve('q?', { collection: 'docs', topN: 4 })
                .then(r => r.text.length + ':' + r.citations.length + ':' + r.citations[0].snippet)
            """).UnwrapIfPromise());

        Assert.Equal("0:1:PASSAGE", result.AsString());
        Assert.Equal(1, pipeline.RetrieveCallCount);
        Assert.Equal(0, pipeline.QueryCallCount);
        Assert.Equal(4, pipeline.LastQuery?.TopN);
    }

    [Fact]
    public async Task Query_StillGenerates_WhenThePipelineAlsoSupportsRetrieval()
    {
        // The two surfaces must stay distinct on the same pipeline instance.
        var pipeline = new FakeRetrievalCapableRagPipeline { Answer = new RagAnswer { Text = "generated" } };
        using var engine = CreateEngine(query: pipeline);

        var result = await Task.Run(() => engine.Evaluate(
            "rag.query('q?', { collection: 'docs' }).then(r => r.text)").UnwrapIfPromise());

        Assert.Equal("generated", result.AsString());
        Assert.Equal(1, pipeline.QueryCallCount);
        Assert.Equal(0, pipeline.RetrieveCallCount);
    }

    [Fact]
    public async Task Retrieve_OnAPipelineThatCannot_FailsLoudly_RatherThanGeneratingAnyway()
    {
        // FakeRagPipeline implements IRagPipeline only — the shape of the
        // corrective graph, which interleaves evaluation with generation.
        var pipeline = new FakeRagPipeline();
        using var engine = CreateEngine(query: pipeline);

        var ex = await Record.ExceptionAsync(() => Task.Run(() => engine.Evaluate(
            "rag.retrieve('q?', { collection: 'docs' })").UnwrapIfPromise()));

        Assert.NotNull(ex);
        var message = FlattenMessage(ex!);
        Assert.Contains("IRagRetrievalCapable", message, StringComparison.Ordinal);
        Assert.Contains("rag.query", message, StringComparison.Ordinal);
        // The refusal must cost nothing: no fallback generation behind it.
        Assert.Equal(0, pipeline.CallCount);
    }

    [Fact]
    public async Task Retrieve_HonoursThePerCallProfile()
    {
        var hostWide = new FakeRetrievalCapableRagPipeline();
        var profiled = new FakeRetrievalCapableRagPipeline();
        var resolver = new RecordingProfileResolver(profiled);
        using var engine = CreateEngine(query: hostWide, profileResolver: resolver);

        await Task.Run(() => engine.Evaluate(
            "rag.retrieve('q?', { collection: 'docs', profile: 'balanced' })").UnwrapIfPromise());

        Assert.Equal(["balanced"], resolver.ResolvedProfiles);
        Assert.Equal(1, profiled.RetrieveCallCount);
        Assert.Equal(0, hostWide.RetrieveCallCount);
    }

    [Fact]
    public async Task Retrieve_WithoutASubsystem_FailsLoudly_WithAnActionableMessage()
    {
        using var engine = CreateEngine();

        var ex = await Record.ExceptionAsync(() => Task.Run(() => engine.Evaluate(
            "rag.retrieve('q?', { collection: 'docs' })").UnwrapIfPromise()));

        Assert.NotNull(ex);
        var message = FlattenMessage(ex!);
        Assert.Contains("AddOrkeonRag", message, StringComparison.Ordinal);
        // The surface name must be its own, not query's — the message is what a
        // script author reads to find the call site.
        Assert.Contains("rag.retrieve", message, StringComparison.Ordinal);
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
