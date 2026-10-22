using Jint;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Scripting;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Bindings;

/// <summary>The REPL host binds <c>rag.*</c> through the shared backend factory (RT-25).</summary>
public sealed class RagNamespaceReplTests
{
    [Fact]
    [Trait("BR", "RT-25")]
    public async Task ShouldReturnCitations_WhenRagRetrieveCalledInReplHost()
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
        services.AddScriptCommands();
        using var provider = services.BuildServiceProvider();
        using var engine = provider.GetRequiredService<JsEngineFactory>().Create();

        var result = await Task.Run(() => engine.Evaluate("""
            rag.retrieve('q', { collection: 'c', topN: 5 })
                .then(r => r.citations.length + ':' + r.citations[0].snippet)
            """).UnwrapIfPromise());
        await Task.Run(() => engine.Evaluate(
            "rag.ingest({ collection: 'c', sources: '/workspace/a.md' })").UnwrapIfPromise());

        Assert.Equal("1:PASSAGE", result.AsString());
        Assert.Equal(1, pipeline.RetrieveCallCount);
        Assert.Equal(5, pipeline.LastQuery?.TopN);
        Assert.Equal("c", pipeline.LastQuery?.Collection);
        // The backend serves the very singleton the container holds (the one rag_ingest uses).
        Assert.Equal(1, ingestion.CallCount);
    }
}
