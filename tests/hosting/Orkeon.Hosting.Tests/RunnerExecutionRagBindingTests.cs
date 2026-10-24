using Jint;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Scripting;
using Orkeon.Scripting.Bindings;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The daemon host binds <c>rag.*</c> (RT-25). <c>RunnerExecution.LoadCrewFromScriptAsync</c> is private and
/// needs a whole IHost; the seam is the exact call it makes: <see cref="RagScriptingBackendFactory.Build"/>
/// over the host's services, handed to <c>JsEngineFactory(ragBackend:)</c>.
/// </summary>
public sealed class RunnerExecutionRagBindingTests
{
    [Fact]
    [Trait("BR", "RT-25")]
    public async Task ShouldBindRagNamespace_WhenRunnerExecutionBuildsEngine()
    {
        var pipeline = new FakeRetrievalCapableRagPipeline
        {
            Retrieved = new RagAnswer
            {
                Text = "",
                Citations = [new Citation { Marker = 1, ChunkId = "c1", SourceId = "/kb/a.md", Snippet = "PASSAGE", Score = 0.9 }],
            },
        };
        var ingestion = new FakeIngestionPipeline();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IIngestionPipeline>(ingestion);
        services.AddSingleton<IRagPipeline>(pipeline);
        using var provider = services.BuildServiceProvider();

        var backend = RagScriptingBackendFactory.Build(provider);
        using var engine = new JsEngineFactory(ragBackend: backend).Create();
        var result = await Task.Run(() => engine.Evaluate("""
            rag.retrieve('q', { collection: 'c', topN: 5 })
                .then(r => r.citations.length + ':' + r.citations[0].snippet)
            """).UnwrapIfPromise());
        await Task.Run(() => engine.Evaluate(
            "rag.ingest({ collection: 'c', sources: '/workspace/a.md' })").UnwrapIfPromise());

        Assert.Equal("1:PASSAGE", result.AsString());
        Assert.Equal(1, pipeline.RetrieveCallCount);
        Assert.Equal(1, ingestion.CallCount);
        Assert.Same(ingestion, backend!.IngestionPipeline);
        Assert.Same(pipeline, backend.RagPipeline);
    }

    [Fact]
    [Trait("BR", "RT-25")]
    public void ShouldLogCause_WhenRagPipelineActivationThrows()
    {
        using var logs = new FakeCapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logs);
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IIngestionPipeline>(new FakeIngestionPipeline());
        services.AddSingleton<IRagPipeline>(_ => throw new InvalidOperationException("no embedding default"));
        using var provider = services.BuildServiceProvider();

        var backend = RagScriptingBackendFactory.Build(provider);

        Assert.Null(backend);
        var entry = Assert.Single(logs.Entries, e => e.Message.Contains("no embedding default", StringComparison.Ordinal));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, entry.Level);
        Assert.Contains("IRagPipeline", entry.Message, StringComparison.Ordinal);
    }
}
