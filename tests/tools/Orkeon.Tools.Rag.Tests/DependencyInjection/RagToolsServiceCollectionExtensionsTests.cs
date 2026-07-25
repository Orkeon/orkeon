using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Rag.DependencyInjection;
using Orkeon.Tools.Rag.Tests.Doubles;

namespace Orkeon.Tools.Rag.Tests.DependencyInjection;

/// <summary>
/// <c>AddOrkeonRagTools()</c> registers <c>rag_search</c> and <c>rag_ingest</c> as
/// <see cref="IBaseTool"/>s discoverable by tool registries
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
        return services;
    }

    [Fact]
    public void AddOrkeonRagTools_RegistersRagSearch_AndRagIngest_AsIBaseTools()
    {
        var services = BaseServices();

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(2, tools.Count);
        var ragSearch = Assert.Single(tools, t => t.Name == "rag_search");
        Assert.IsType<RagSearchTool>(ragSearch);
        var ragIngest = Assert.Single(tools, t => t.Name == "rag_ingest");
        Assert.IsType<RagIngestTool>(ragIngest);
    }

    [Fact]
    public void AddOrkeonRagTools_CohabitsWithOtherToolRegistrations()
    {
        var services = BaseServices();
        services.AddSingleton<IBaseTool>(new Orkeon.Tests.Shared.Doubles.StubBaseTool("other_tool"));

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, t => t.Name == "rag_search");
        Assert.Contains(tools, t => t.Name == "rag_ingest");
        Assert.Contains(tools, t => t.Name == "other_tool");
    }
}
