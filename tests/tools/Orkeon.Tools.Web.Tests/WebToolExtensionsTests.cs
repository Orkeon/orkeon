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
        // image_generation reads its OpenAI key from the secret provider (GAP-09).
        services.AddSingleton<Orkeon.Application.Interfaces.Security.ISecretProvider>(new MockSecretProvider());
        services.AddOrkeonWebTools();
        using var sp = services.BuildServiceProvider();

        var tools = sp.GetServices<IBaseTool>().ToList();

        Assert.Contains(tools, t => t is HttpApiTool);
        Assert.Contains(tools, t => t is WebScrapeTool);
        Assert.Contains(tools, t => t is ScrapeElementTool);
        Assert.Contains(tools, t => t is GitHubTool);
        Assert.Contains(tools, t => t is ImageGenerationTool);
        Assert.Contains(tools, t => t is FirefoxTool);
    }

    [Fact]
    public void AddOrkeonWebTools_HandsEveryFirefoxToolTheBrowsersOfTheHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<Orkeon.Application.Interfaces.Security.ISecretProvider>(new MockSecretProvider());
        services.AddOrkeonWebTools();
        using var sp = services.BuildServiceProvider();

        // A crew resolves the tools each time it loads: the browsers must not start over with them.
        var first = sp.GetServices<IBaseTool>().OfType<FirefoxTool>().Single();
        var second = sp.GetServices<IBaseTool>().OfType<FirefoxTool>().Single();

        Assert.NotSame(first, second);
        Assert.Same(first.Sessions, second.Sessions);
    }

    [Fact]
    public async Task AddOrkeonWebTools_RegistersFirefox_WhichRefusesALoopbackPageBeforeAnyBrowserStarts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<Orkeon.Application.Interfaces.Security.ISecretProvider>(new MockSecretProvider());
        services.AddOrkeonWebTools();
        using var sp = services.BuildServiceProvider();

        var firefox = sp.GetServices<IBaseTool>().OfType<FirefoxTool>().Single();
        var refused = await firefox.CallAsync(
            new Orkeon.Domain.Tools.Protocol.ToolCallRequest(
                ToolName: "firefox",
                Parameters: new Dictionary<string, object?> { ["action"] = "navigate", ["url"] = "http://127.0.0.1:9/" }),
            TestContext.Current.CancellationToken);

        // Registered and built without a browser, a display or the RAG subsystem; with no URL policy
        // registered the fail-closed guard applies, and refuses a loopback address before Firefox starts.
        Assert.Equal("firefox", firefox.Name);
        Assert.False(refused.Success);
        Assert.Contains("was not opened", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOrkeonWebTools_UsesRagWebScrape_WhenEmbeddingAndMemoryRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingService>(new FakeEmbeddingService());
        services.AddSingleton<IMemoryProvider>(new FakeMemoryProvider());
        // ImageGenerationTool now requires an IFileSystemService (VFS-70); register a double.
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        // image_generation reads its OpenAI key from the secret provider (GAP-09).
        services.AddSingleton<Orkeon.Application.Interfaces.Security.ISecretProvider>(new MockSecretProvider());
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
