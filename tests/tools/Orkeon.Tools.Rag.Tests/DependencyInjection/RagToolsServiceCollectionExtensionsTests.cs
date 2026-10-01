using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Evaluation;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Rag.DependencyInjection;
using Orkeon.Tools.Rag.Tests.Doubles;

namespace Orkeon.Tools.Rag.Tests.DependencyInjection;

/// <summary>
/// <c>AddOrkeonRagTools()</c> registers <c>rag_search</c>, <c>rag_ingest</c> and
/// <c>rag_eval</c> as <see cref="IBaseTool"/>s discoverable by tool registries
/// (<c>GetServices&lt;IBaseTool&gt;()</c>).
/// </summary>
public class RagToolsServiceCollectionExtensionsTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRagPipeline>(new FakeRagPipeline());
        services.AddSingleton<IIngestionPipeline>(new FakeIngestionPipeline());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IRagEvalHarness>(new FakeRagEvalHarness());
        return services;
    }

    [Fact]
    public void AddOrkeonRagTools_RegistersRagSearch_RagIngest_AndRagEval_AsIBaseTools()
    {
        var services = BaseServices();

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(3, tools.Count);
        var ragSearch = Assert.Single(tools, t => t.Name == "rag_search");
        Assert.IsType<RagSearchTool>(ragSearch);
        var ragIngest = Assert.Single(tools, t => t.Name == "rag_ingest");
        Assert.IsType<RagIngestTool>(ragIngest);
        var ragEval = Assert.Single(tools, t => t.Name == "rag_eval");
        Assert.IsType<RagEvalTool>(ragEval);
    }

    [Fact]
    public void AddOrkeonRagTools_CohabitsWithOtherToolRegistrations()
    {
        var services = BaseServices();
        services.AddSingleton<IBaseTool>(new Orkeon.Tests.Shared.Doubles.StubBaseTool("other_tool"));

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(4, tools.Count);
        Assert.Contains(tools, t => t.Name == "rag_search");
        Assert.Contains(tools, t => t.Name == "rag_ingest");
        Assert.Contains(tools, t => t.Name == "rag_eval");
        Assert.Contains(tools, t => t.Name == "other_tool");
    }

    [Fact]
    public void AddOrkeonRagTools_IsIdempotent()
    {
        // GAP-02: every runner host registers the tools; a host that also calls the
        // extension must not end up with two rag_search (the DI registry refuses that).
        var services = BaseServices();

        services.AddOrkeonRagTools();
        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        Assert.Equal(3, provider.GetServices<IBaseTool>().Count());
    }

    [Fact]
    public async Task AddOrkeonRagTools_ToolsAreBuiltWithoutResolvingTheRagSubsystem()
    {
        // GAP-02: a tool registry builds every IBaseTool for every crew. Building the RAG
        // tools must not resolve the pipelines (store, its provider, embeddings): a crew
        // that never calls them must not pay for — or fail on — the subsystem.
        var services = new ServiceCollection();
        services.AddSingleton<IRagPipeline>(_ => throw new InvalidOperationException("pipeline resolved"));
        services.AddSingleton<IIngestionPipeline>(_ => throw new InvalidOperationException("ingestion resolved"));
        services.AddSingleton<IRagEvalHarness>(_ => throw new InvalidOperationException("harness resolved"));
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton(new Orkeon.Rag.Abstractions.Options.RagOptions { Collection = "produits" });

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();
        Assert.Equal(3, tools.Count);

        // The first call is where the subsystem is resolved — and where it fails.
        var search = Assert.Single(tools, t => t.Name == "rag_search");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => search.CallAsync(
            new Orkeon.Domain.Tools.Protocol.ToolCallRequest("rag_search",
                new Dictionary<string, object?> { ["question"] = "q" }),
            TestContext.Current.CancellationToken));
        Assert.Equal("pipeline resolved", error.Message);
    }

    [Fact]
    public async Task AddOrkeonRagTools_RagSearchFallsBackToTheConfiguredCollection()
    {
        var pipeline = new FakeRagPipeline();
        var services = new ServiceCollection();
        services.AddSingleton<IRagPipeline>(pipeline);
        services.AddSingleton<IIngestionPipeline>(new FakeIngestionPipeline());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<IRagEvalHarness>(new FakeRagEvalHarness());
        services.AddSingleton(new Orkeon.Rag.Abstractions.Options.RagOptions { Collection = "produits" });

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var search = Assert.Single(provider.GetServices<IBaseTool>(), t => t.Name == "rag_search");
        await search.CallAsync(
            new Orkeon.Domain.Tools.Protocol.ToolCallRequest("rag_search",
                new Dictionary<string, object?> { ["question"] = "q" }),
            TestContext.Current.CancellationToken);

        Assert.Equal("produits", pipeline.LastQuery?.Collection);
    }
}
