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
/// #445 - the caller's runs on the bridge: one scope per caller run in the caller's session, ended with the run, or
/// with the session when the caller stays silent past the heartbeat window; the heartbeat while the session is open;
/// a run whose session was lost fails, and a later run opens a new session.
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
        Assert.Equal((true, 0, 0), (Assert.Single(remote.RunResources).IsDisposed, server.RunsOf(link.Client!.SessionId), link.Client.LiveRuns));
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
        Assert.Equal(0, server.RunsOf(link.Client!.SessionId));
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
        using var http = BridgeLoopback.Plain(link.Client!.Address);
        using var beat = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.HeartbeatEndpoint, link.Client.SessionId, "{}");

        // Act
        clock.Advance(Window * 3 / 4);

        // Assert
        Assert.Equal((1, false), (server.RunsOf(link.Client.SessionId), remote.RunResources.Single().IsDisposed));
    }

    [Fact]
    public async Task ShouldFailTheCallAsSessionLost_WhenTheBridgeEndedTheSessionOfALiveRun()
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
        var answer = await bridged.CallAsync(Read("firefox"), Ct);
        var again = await bridged.CallAsync(Read("firefox"), Ct);

        // Assert
        Assert.Equal(
            (false, nameof(ToolBridgeFailure.SessionLost), false, nameof(ToolBridgeFailure.SessionLost), 1),
            (answer.Success, answer.Metadata?[ToolBridgeProxy.BridgeFailureKey]?.ToString(),
                again.Success, again.Metadata?[ToolBridgeProxy.BridgeFailureKey]?.ToString(), remote.Calls.Count));
        Assert.Contains("lost this caller's session", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldOpenANewSession_WhenTheRunsOfTheLostSessionHaveEnded()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var remote = new FakeBridgeTool("firefox");
        var (server, link, bridged) = await BridgeAsync(remote, serverTime: clock);
        await using var _ = server;
        using var __ = link;
        var lostRun = AgentRunScope.Begin("agent", "tester");
        await bridged.CallAsync(Read("firefox"), Ct);
        var lost = link.Client!.SessionId;
        clock.Advance(Window + Window);
        await Polling.WaitUntilAsync(() => remote.RunResources.Single().IsDisposed);
        await bridged.CallAsync(Read("firefox"), Ct);
        await lostRun.DisposeAsync();
        await using var run = AgentRunScope.Begin("agent", "tester");

        // Act
        var answer = await bridged.CallAsync(Read("firefox"), Ct);

        // Assert
        Assert.Equal((true, 2, 1), (answer.Success, remote.RunResources.Count, server.RunsOf(link.Client.SessionId)));
        Assert.NotEqual(lost, link.Client.SessionId);
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
    public async Task ShouldStopTheHeartbeat_WhenTheBridgeAnswersThatItLostTheSession()
    {
        // Arrange
        var callerClock = new ManualTimeProvider();
        var serverClock = new ManualTimeProvider();
        var (server, link, bridged) = await BridgeAsync(new FakeBridgeTool("firefox"), serverTime: serverClock, callerTime: callerClock);
        await using var _ = server;
        using var __ = link;
        await bridged.CallAsync(Read("firefox"), Ct);
        serverClock.Advance(Window + Window);
        await Polling.WaitUntilAsync(() => server.Sessions == 0);
        callerClock.Advance(Window / 3);
        await Polling.WaitUntilAsync(() => link.Client!.SessionId is null);
        var sent = link.Client!.HeartbeatsSent;

        // Act
        callerClock.Advance(Window);

        // Assert
        Assert.Equal((1, 1), (sent, link.Client.HeartbeatsSent));
    }
}
