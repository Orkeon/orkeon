using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Network;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the caller's side, against a real server in the same process: what is bridged, the refusals of the start, a
/// lost bridge and the cancellation of a call.
/// </summary>
public sealed class ToolBridgeLinkTests
{
    private static ToolCallRequest Read(string tool) => new(tool, new Dictionary<string, object?> { ["action"] = "read" });

    [Fact]
    public async Task ShouldRunTheCallOnTheBridge_WhenTheToolIsBridged()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox") { Response = new ToolCallResponse(true, "from the bridge", null) };
        var (server, address) = await BridgeLoopback.StartServerAsync([remote], ["*"]);
        await using var _ = server;
        var local = new FakeBridgeTool("firefox") { Response = new ToolCallResponse(true, "local", null) };
        var registry = new ToolRegistry([local]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);

        // Act
        var response = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(("from the bridge", 0), (response.Result, local.Calls.Count));
    }

    [Fact]
    public async Task ShouldBridgeWhatTheBridgeServes_WhenAllowIsTheLoneStar()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox"), new FakeBridgeTool("teams_chat")]);

        // Act
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "*"));

        // Assert
        Assert.Equal(["firefox"], link.BridgedTools);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenTheBridgeCannotBeReached()
    {
        // Arrange
        using var refusing = LoopbackPorts.Refusing();
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        var options = BridgeLoopback.ClientOptions(LoopbackPorts.BaseAddress(refusing.Port), "firefox");

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(() => BridgeLoopback.ConnectAsync(registry, options));

        // Assert
        Assert.Contains("cannot be reached", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenTheBridgeRefusesTheToken()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(
            () => BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"), token: "wrong"));

        // Assert
        Assert.Contains("refused the token", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenANamedToolIsNotServed()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox"), new FakeBridgeTool("teams_chat")]);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(
            () => BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "teams_chat")));

        // Assert
        Assert.Contains("'teams_chat'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenAContractHashDiffers()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox") { WriteAccess = ToolAccess.Execute }]);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(
            () => BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox")));

        // Assert
        Assert.Contains("contract of 'firefox' differs", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepTheLocalAccessClasses_WhenAToolIsBridged()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var local = new FakeBridgeTool("firefox");
        var registry = new ToolRegistry([local]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));

        // Act
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(local, Assert.IsType<ToolBridgeProxy>(bridged).Inner);
        Assert.Equal(ToolAccess.Edit, bridged!.AccessFor(new Dictionary<string, object?> { ["action"] = "write" }));
    }

    [Fact]
    public async Task ShouldFailTheCallWithTheReason_WhenTheBridgeIsLost()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        var local = new FakeBridgeTool("firefox");
        var registry = new ToolRegistry([local]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);
        await server.DisposeAsync();

        // Act
        var response = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(nameof(ToolBridgeFailure.Unreachable), response.Metadata![ToolBridgeProxy.BridgeFailureKey]);
        Assert.Empty(local.Calls);
    }

    [Fact]
    public async Task ShouldReachTheBridgeAgain_WhenItComesBackAfterAFailedCall()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);
        await server.DisposeAsync();
        _ = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);
        var (again, _) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"], port: address.Port);
        await using var __ = again;

        // Act
        var response = await bridged.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success, response.Error);
    }

    [Fact]
    public async Task ShouldCancelTheCallOnTheBridge_WhenTheCallerCancelsIt()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox") { WaitsForCancellation = true };
        var (server, address) = await BridgeLoopback.StartServerAsync([remote], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var call = bridged!.CallAsync(Read("firefox"), cancellation.Token);
        await remote.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await cancellation.CancelAsync();

        // Assert
        var response = await call.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(true, response.Metadata!["cancelled"]);
        await remote.Cancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
    }
}
