using System.Net.Http.Headers;
using System.Net.Http.Json;
using Orkeon.Domain.Execution;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Timing;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the caller's runs on the bridge: one scope per caller run, ended with the run, or at once when the caller
/// stays silent past the heartbeat window; the heartbeat of the caller while a run is live.
/// </summary>
public sealed class ToolBridgeRunTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ToolCallRequest Read(string tool) => new(tool, new Dictionary<string, object?> { ["action"] = "read" });

    private static async Task<(ToolBridgeServer Server, ToolBridgeLink Link, IBaseTool Bridged)> BridgeAsync(
        FakeBridgeTool remote, TimeProvider? serverTime = null, TimeProvider? callerTime = null)
    {
        var (server, address) = await BridgeLoopback.StartServerAsync([remote], ["*"], time: serverTime);
        var registry = new ToolRegistry([new FakeBridgeTool(remote.Name)]);
        var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, remote.Name), time: callerTime);
        return (server, link, (await registry.GetToolByNameAsync(remote.Name, Ct))!);
    }

    [Fact]
    public async Task ShouldRunTheCallsOfOneCallerRunInOneBridgeScope_WhenTheyShareTheRun()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox");
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;
        await using var run = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);

        // Act
        await bridged.CallAsync(Read("firefox"), Ct);

        // Assert
        var scopes = remote.Runs.ToArray();
        Assert.Equal((2, 1, run.RunId), (scopes.Length, scopes.Distinct().Count(), scopes[0]?.AgentId));
    }

    [Fact]
    public async Task ShouldRunTheCallOutsideAnyScope_WhenTheCallerIsInNoRun()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox");
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;

        // Act
        await bridged.CallAsync(Read("firefox"), Ct);

        // Assert
        Assert.Null(Assert.Single(remote.Runs));
    }

    [Fact]
    public async Task ShouldEndTheBridgeScope_WhenTheCallerRunEnds()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox");
        var (server, link, bridged) = await BridgeAsync(remote);
        await using var _ = server;
        using var __ = link;
        var run = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);

        // Act
        await run.DisposeAsync();

        // Assert
        Assert.Equal((true, 0, 0), (Assert.Single(remote.RunResources).IsDisposed, server.RunsOf(link.Client!.CallerId), link.Client.LiveRuns));
    }

    [Fact]
    public async Task ShouldEndTheRunsOfTheCaller_WhenItGivesNoSignForTheHeartbeatWindow()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var remote = new FakeBridgeTool("firefox");
        var (server, link, bridged) = await BridgeAsync(remote, serverTime: clock);
        await using var _ = server;
        using var __ = link;
        await using var run = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);

        // Act
        clock.Advance(Window + Window);

        // Assert
        await Polling.WaitUntilAsync(() => remote.RunResources.Single().IsDisposed);
        Assert.Equal(0, server.RunsOf(link.Client!.CallerId));
    }

    [Fact]
    public async Task ShouldKeepTheRunsOfTheCaller_WhenAHeartbeatCameWithinTheWindow()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var remote = new FakeBridgeTool("firefox");
        var (server, link, bridged) = await BridgeAsync(remote, serverTime: clock);
        await using var _ = server;
        using var __ = link;
        await using var run = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);
        clock.Advance(Window / 2);
        using var http = new HttpClient { BaseAddress = new Uri(link.Client!.Address, ToolBridgeProtocol.BasePath) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BridgeLoopback.Token);
        using var beat = await http.PostAsJsonAsync(ToolBridgeProtocol.HeartbeatEndpoint,
            new BridgeHeartbeatRequest { CallerId = link.Client.CallerId }, ToolBridgeProtocol.JsonOptions, Ct);

        // Act
        clock.Advance(Window * 3 / 4);

        // Assert
        Assert.Equal((1, false), (server.RunsOf(link.Client.CallerId), remote.RunResources.Single().IsDisposed));
    }

    [Fact]
    public async Task ShouldGiveANewScope_WhenTheCallerComesBackAfterItsRunsWereEnded()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var remote = new FakeBridgeTool("firefox");
        var (server, link, bridged) = await BridgeAsync(remote, serverTime: clock);
        await using var _ = server;
        using var __ = link;
        await using var run = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);
        clock.Advance(Window + Window);
        await Polling.WaitUntilAsync(() => remote.RunResources.Single().IsDisposed);

        // Act
        await bridged.CallAsync(Read("firefox"), Ct);

        // Assert
        var scopes = remote.Runs.ToArray();
        Assert.Equal((2, false), (remote.RunResources.Count, ReferenceEquals(scopes[0], scopes[1])));
    }

    [Fact]
    public async Task ShouldSendAHeartbeat_WhileACallerRunIsLiveOnTheBridge()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var (server, link, bridged) = await BridgeAsync(new FakeBridgeTool("firefox"), callerTime: clock);
        await using var _ = server;
        using var __ = link;
        await using var run = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);

        // Act
        clock.Advance(Window / 3);

        // Assert
        Assert.Equal(1, link.Client!.HeartbeatsSent);
    }

    [Fact]
    public async Task ShouldStopTheHeartbeat_WhenTheLastCallerRunEnds()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var (server, link, bridged) = await BridgeAsync(new FakeBridgeTool("firefox"), callerTime: clock);
        await using var _ = server;
        using var __ = link;
        var run = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);
        await run.DisposeAsync();

        // Act
        clock.Advance(Window);

        // Assert
        Assert.Equal(0, link.Client!.HeartbeatsSent);
    }
}
