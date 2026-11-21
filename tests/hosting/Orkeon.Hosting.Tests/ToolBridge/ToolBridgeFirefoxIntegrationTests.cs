using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Common;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Web;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the real <c>firefox</c> tool served by the bridge, headless, against a page on the loopback: the page is read
/// through the bridge, the screenshot lands in the caller's file system, the browser ends with the caller's run.
/// Skipped where Firefox is not installed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ToolBridgeFirefoxIntegrationTests : IDisposable
{
    private const string Page = "<!doctype html><html><head><title>Bridged page</title></head><body><h1>Over the bridge</h1></body></html>";

    private readonly HttpListener _listener;
    private readonly Uri _home;
    private readonly Task _pump;
    private readonly FirefoxTool _remote;
    private readonly FirefoxTool _local;
    private readonly BridgeFileSystem _bridgeFiles = new();
    private readonly FakeFileSystemService _callerFiles = new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite);
    private readonly FirefoxToolOptions _options = new()
    {
        Headless = true,
        StartupTimeoutSeconds = (int)HangGuards.ProcessStart.TotalSeconds,
    };

    public ToolBridgeFirefoxIntegrationTests()
    {
        // What AddOrkeonInfrastructure does in a host: the tool's response is serialized through it.
        if (!ComponentBase.IsDefaultSerializerConfigured)
            ComponentBase.DefaultSerializer = JsonComponentSerializer.Instance;
        (_listener, _home) = LoopbackPorts.StartListener();
        _pump = ServeAsync();
        _remote = new FirefoxTool(_options, _bridgeFiles.Service, new AllowAllUrlValidator());
        _local = new FirefoxTool(_options, _callerFiles, new AllowAllUrlValidator());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _listener.Close();
        _pump.GetAwaiter().GetResult();
        _remote.Dispose();
        _local.Dispose();
        _bridgeFiles.Dispose();
    }

    private async Task<(ToolBridgeServer Server, ToolBridgeLink Link, IBaseTool Bridged)> BridgeAsync()
    {
        var (server, address) = await BridgeLoopback.StartServerAsync([_remote], ["*"], files: _bridgeFiles);
        var registry = new ToolRegistry([_local]);
        var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"), fileSystem: _callerFiles);
        return (server, link, (await registry.GetToolByNameAsync("firefox", Ct))!);
    }

    private static async Task<JsonObject> CallAsync(IBaseTool tool, string action, params (string Name, object? Value)[] arguments)
    {
        var parameters = new Dictionary<string, object?> { ["action"] = action };
        foreach (var (name, value) in arguments)
            parameters[name] = value;
        var response = await tool.CallAsync(new ToolCallRequest("firefox", parameters), Ct);
        Assert.SkipWhen(response.Error?.Contains("Firefox was not found", StringComparison.Ordinal) == true, "Firefox is not installed on this machine.");
        Assert.True(response.Success, response.Error);
        return Assert.IsType<JsonObject>(JsonSerializer.SerializeToNode(response.Result));
    }

    [Fact]
    public async Task ShouldReadThePage_WhenFirefoxRunsOnTheBridge()
    {
        // Arrange
        var (server, link, bridged) = await BridgeAsync();
        await using var _ = server;
        using var __ = link;
        await using var run = AgentRunScope.Begin("agent", "tester");

        // Act
        var page = await CallAsync(bridged, "navigate", ("url", _home.ToString()));

        // Assert
        Assert.Equal("Bridged page", page["title"]?.GetValue<string>());
    }

    [Fact]
    public async Task ShouldWriteTheScreenshotOnTheCaller_WhenFirefoxTookItOnTheBridge()
    {
        // Arrange
        var (server, link, bridged) = await BridgeAsync();
        await using var _ = server;
        using var __ = link;
        await using var run = AgentRunScope.Begin("agent", "tester");
        await CallAsync(bridged, "navigate", ("url", _home.ToString()));

        // Act
        await CallAsync(bridged, "screenshot", ("save_to_path", "/output/page.png"));

        // Assert
        var png = await _callerFiles.TryReadAllBytesAsync("/output/page.png", Ct);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png?[..4]);
    }

    [Fact]
    public async Task ShouldEndTheBrowserOnTheBridge_WhenTheCallerRunEnds()
    {
        // Arrange
        var (server, link, bridged) = await BridgeAsync();
        await using var _ = server;
        using var __ = link;
        var run = AgentRunScope.Begin("agent", "tester");
        await CallAsync(bridged, "navigate", ("url", _home.ToString()));

        // Act
        await run.DisposeAsync();

        // Assert
        Assert.Equal(0, server.RunsOf(link.Client!.CallerId));
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(Page)).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
            {
                // The browser left before the answer.
            }
        }
    }
}
