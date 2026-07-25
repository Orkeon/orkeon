using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Tools.Rag.DependencyInjection;
using Orkeon.Tools.Rag.Tests.Doubles;

namespace Orkeon.Tools.Rag.Tests.DependencyInjection;

/// <summary>
/// <c>AddOrkeonRagTools()</c> registers <c>rag_search</c> as an <see cref="IBaseTool"/>
/// discoverable by tool registries (<c>GetServices&lt;IBaseTool&gt;()</c>).
/// </summary>
public class RagToolsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOrkeonRagTools_RegistersRagSearch_AsIBaseTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRagPipeline>(new FakeRagPipeline());

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        var ragSearch = Assert.Single(tools, t => t.Name == "rag_search");
        Assert.IsType<RagSearchTool>(ragSearch);
    }

    [Fact]
    public void AddOrkeonRagTools_CohabitsWithOtherToolRegistrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRagPipeline>(new FakeRagPipeline());
        services.AddSingleton<IBaseTool>(new Orkeon.Tests.Shared.Doubles.StubBaseTool("other_tool"));

        services.AddOrkeonRagTools();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "rag_search");
        Assert.Contains(tools, t => t.Name == "other_tool");
    }
}
