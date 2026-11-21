using Orkeon.Hosting.ToolBridge;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the tool host of <c>orkeon mcp serve</c> and <c>--list-tools</c> connects the bridge its settings name, or is
/// refused, and the host of <c>orkeon bridge serve</c> never dials one. Joins <see cref="ConsoleSerialCollection"/>
/// because the host build writes on the console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class ToolBridgeHostTests : IDisposable
{
    private readonly string _root = RegisteredTools.CreateRoot("orkeon-bridge-host-");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private void WriteSettings(int port) => File.WriteAllText(Path.Combine(_root, "appsettings.json"), $$"""
        { "Orkeon": { "Tools": { "Bridge": {
            "Address": "http://127.0.0.1:{{port}}/",
            "Allow": [ "web_search" ],
            "TokenEnvVar": "ORKEON_TEST_BRIDGE_HOST_TOKEN_UNSET" } } } }
        """);

    private async Task<(Microsoft.Extensions.Hosting.IHost? Host, string Error)> BuildAsync(bool serving)
    {
        using var error = new StringWriter();
        var host = await RunnerExecution.BuildToolHostAsync(
            Path.Combine(_root, "appsettings.json"),
            _root,
            new ToolHostMounts([], [], AllowExternalMounts: true, OfferedMountSelectors.MountIdOption | OfferedMountSelectors.MountOption),
            (_, services) =>
            {
                if (serving)
                    ToolBridgeStartup.RegisterServing(services);
            },
            error,
            TestContext.Current.CancellationToken);
        return (host, error.ToString());
    }

    [Fact]
    public async Task ShouldRefuseTheHost_WhenItsSettingsNameABridgeItCannotConnect()
    {
        // Arrange
        using var refusing = LoopbackPorts.Refusing();
        WriteSettings(refusing.Port);

        // Act
        var (host, error) = await BuildAsync(serving: false);
        using var _ = host;

        // Assert
        Assert.Null(host);
        Assert.Contains("tool bridge", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNeverDialABridge_WhenTheHostServesTheBridge()
    {
        // Arrange
        using var refusing = LoopbackPorts.Refusing();
        WriteSettings(refusing.Port);

        // Act
        var (host, error) = await BuildAsync(serving: true);
        using var _ = host;

        // Assert
        Assert.NotNull(host);
        Assert.DoesNotContain("tool bridge", error, StringComparison.Ordinal);
    }
}
