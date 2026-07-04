using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.DependencyInjection;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// Verifies the DI registration extensions resolve the expected tool implementations.
/// </summary>
public class WebToolExtensionsTests
{
    [Fact]
    public void AddOrkeonWebTools_RegistersCoreTools_WithoutOptionalServices()
    {
        var services = new ServiceCollection();
        // ImageGenerationTool now requires an IFileSystemService (VFS-70); register a double.
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonWebTools();
        using var sp = services.BuildServiceProvider();

        var tools = sp.GetServices<IBaseTool>().ToList();

        Assert.Contains(tools, t => t is HttpApiTool);
        Assert.Contains(tools, t => t is WebScrapeTool);
        Assert.Contains(tools, t => t is ScrapeElementTool);
        Assert.Contains(tools, t => t is GitHubTool);
        Assert.Contains(tools, t => t is ImageGenerationTool);
    }

    [Fact]
    public void AddOrkeonWebTools_UsesRagWebScrape_WhenEmbeddingAndMemoryRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingService>(new FakeEmbeddingService());
        services.AddSingleton<IMemoryProvider>(new FakeMemoryProvider());
        // ImageGenerationTool now requires an IFileSystemService (VFS-70); register a double.
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonWebTools();
        using var sp = services.BuildServiceProvider();

        var scrape = sp.GetServices<IBaseTool>().OfType<WebScrapeTool>().Single();

        // Tool resolves successfully via the RAG-enabled ctor branch.
        Assert.Equal("web_scrape", scrape.Name);
    }

    [Fact]
    public void AddOrkeonSlackTool_RegistersSlackTool()
    {
        var services = new ServiceCollection();
        services.AddOrkeonSlackTool("xoxb-token");
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<IBaseTool>(), t => t is SlackTool);
    }

    [Fact]
    public void AddOrkeonSlackReadTool_RegistersSlackReadTool()
    {
        var services = new ServiceCollection();
        services.AddOrkeonSlackReadTool("xoxb-token");
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<IBaseTool>(), t => t is SlackReadTool);
    }

    [Fact]
    public void AddOrkeonBraveSearchTool_RegistersBraveTool()
    {
        var services = new ServiceCollection();
        services.AddOrkeonBraveSearchTool("brave-key");
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<IBaseTool>(), t => t is BraveSearchTool);
    }

    [Fact]
    public void AddOrkeonWebSearchTool_RegistersWebSearchTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Orkeon.Application.Interfaces.Security.ISecretProvider>(new MockSecretProvider());
        services.AddOrkeonWebSearchTool();
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<IBaseTool>(), t => t is WebSearchTool);
    }

    [Fact]
    public void AddOrkeonCacheSearchTool_RegistersCacheSearchTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingService>(new FakeEmbeddingService());
        services.AddSingleton<IMemoryProvider>(new FakeMemoryProvider());
        services.AddOrkeonCacheSearchTool();
        using var sp = services.BuildServiceProvider();

        Assert.Contains(sp.GetServices<IBaseTool>(), t => t is CacheSearchTool);
    }
}
