using System.Net;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the sessions the bridge issues: an unguessable id per caller, required on every request but the list and the
/// opening, each session's runs and calls out of reach of another, and the bounds on sessions, runs and calls.
/// </summary>
public sealed class ToolBridgeSessionTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Call(string callId, string? runId = null) => runId is null
        ? $$$"""{"call_id":"{{{callId}}}","tool":"firefox","arguments":{"action":"read"}}"""
        : $$$"""{"call_id":"{{{callId}}}","tool":"firefox","arguments":{"action":"read"},"run_id":"{{{runId}}}"}""";

    [Fact]
    public async Task ShouldIssueDistinctIdsOfAtLeast128Bits_WhenTwoSessionsOpen()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);

        // Act
        var first = await BridgeLoopback.OpenSessionAsync(http);
        var second = await BridgeLoopback.OpenSessionAsync(http);

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.True(first.Length * 4 >= 128, $"a session id of {first.Length * 4} bits");
        Assert.Equal(2, server.Sessions);
    }

    [Fact]
    public async Task ShouldRefuseTheCall_WhenItNamesNoSession()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox");
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"]);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);

        // Act
        using var response = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, null, Call("c1"));

        // Assert
        Assert.Equal((HttpStatusCode.BadRequest, 0), (response.StatusCode, tool.Calls.Count));
    }

    [Fact]
    public async Task ShouldAnswerSessionLost_WhenTheSessionIsUnknown()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox");
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"]);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);

        // Act
        using var response = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, new string('a', 64), Call("c1"));

        // Assert
        Assert.Equal((ToolBridgeProtocol.SessionLostStatus, 0), (response.StatusCode, tool.Calls.Count));
    }

    [Fact]
    public async Task ShouldKeepTheRunOfASession_WhenAnotherSessionEndsTheSameRunId()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox");
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"]);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);
        var owner = await BridgeLoopback.OpenSessionAsync(http);
        var other = await BridgeLoopback.OpenSessionAsync(http);
        using var called = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, owner, Call("c1", "r1"));

        // Act
        using var ended = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.EndRunEndpoint, other, """{"run_id":"r1"}""");

        // Assert
        Assert.Equal((HttpStatusCode.OK, 1, false), (ended.StatusCode, server.RunsOf(owner), tool.RunResources.Single().IsDisposed));
    }

    [Fact]
    public async Task ShouldRunTheCallInItsOwnScope_WhenItNamesTheRunIdOfAnotherSession()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox");
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"]);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);
        var owner = await BridgeLoopback.OpenSessionAsync(http);
        var other = await BridgeLoopback.OpenSessionAsync(http);
        using var first = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, owner, Call("c1", "r1"));

        // Act
        using var second = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, other, Call("c1", "r1"));

        // Assert
        var scopes = tool.Runs.ToArray();
        Assert.Equal((HttpStatusCode.OK, 2, false), (second.StatusCode, tool.RunResources.Count, ReferenceEquals(scopes[0], scopes[1])));
        Assert.Equal((1, 1), (server.RunsOf(owner), server.RunsOf(other)));
    }

    [Fact]
    public async Task ShouldLeaveTheCallOfASessionRunning_WhenAnotherSessionCancelsItsCallId()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox") { WaitsForCancellation = true };
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"]);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);
        var owner = await BridgeLoopback.OpenSessionAsync(http);
        var other = await BridgeLoopback.OpenSessionAsync(http);
        var call = Task.Run(async () =>
        {
            using var answered = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, owner, Call("c1"));
            return answered.StatusCode;
        }, Ct);
        await tool.Started.Task.WaitAsync(Ct);

        // Act
        using var foreign = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CancelEndpoint, other, """{"call_id":"c1"}""");

        // Assert
        var cancelledByOther = tool.Cancelled.Task.IsCompleted;
        using var own = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CancelEndpoint, owner, """{"call_id":"c1"}""");
        await tool.Cancelled.Task.WaitAsync(Ct);
        await call;
        Assert.Equal((HttpStatusCode.OK, false), (foreign.StatusCode, cancelledByOther));
    }

    [Fact]
    public async Task ShouldEndASession_WhenOnlyAnotherSessionBeatsWithinTheWindow()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var tool = new FakeBridgeTool("firefox");
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"], time: clock);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);
        var silent = await BridgeLoopback.OpenSessionAsync(http);
        var beating = await BridgeLoopback.OpenSessionAsync(http);
        using var called = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, silent, Call("c1", "r1"));
        clock.Advance(Window / 2);
        using var beat = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.HeartbeatEndpoint, beating, "{}");

        // Act
        clock.Advance(Window * 3 / 4);

        // Assert
        await Polling.WaitUntilAsync(() => tool.RunResources.Single().IsDisposed);
        using var late = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.HeartbeatEndpoint, silent, "{}");
        using var alive = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.HeartbeatEndpoint, beating, "{}");
        Assert.Equal((ToolBridgeProtocol.SessionLostStatus, HttpStatusCode.OK, 1), (late.StatusCode, alive.StatusCode, server.Sessions));
    }

    [Fact]
    public async Task ShouldRefuseASession_WhenTheBridgeHoldsMaxSessions()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"], configure: o => o.MaxSessions = 1);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);
        await BridgeLoopback.OpenSessionAsync(http);

        // Act
        using var refused = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.SessionEndpoint, null, "{}");

        // Assert
        Assert.Equal((HttpStatusCode.ServiceUnavailable, 1), (refused.StatusCode, server.Sessions));
        Assert.Contains("MaxSessions", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseARun_WhenTheSessionHoldsMaxRunsPerSession()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox");
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"], configure: o => o.MaxRunsPerSession = 1);
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);
        var session = await BridgeLoopback.OpenSessionAsync(http);
        using var first = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, session, Call("c1", "r1"));

        // Act
        using var second = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, session, Call("c2", "r2"));

        // Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.TooManyRequests, 1), (first.StatusCode, second.StatusCode, tool.Calls.Count));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldRefuseACall_WhenTheCallsInFlightReachTheirBound(bool sameSession)
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox") { WaitsForCancellation = true };
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"], configure: o =>
        {
            if (sameSession)
                o.MaxCallsPerSession = 1;
            else
                o.MaxConcurrentCalls = 1;
        });
        await using var _ = server;
        using var http = BridgeLoopback.Plain(address);
        var first = await BridgeLoopback.OpenSessionAsync(http);
        var second = sameSession ? first : await BridgeLoopback.OpenSessionAsync(http);
        var call = Task.Run(async () =>
        {
            using var answered = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, first, Call("c1"));
            return answered.StatusCode;
        }, Ct);
        await tool.Started.Task.WaitAsync(Ct);

        // Act
        using var refused = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CallEndpoint, second, Call("c2"));

        // Assert
        using var cancel = await BridgeLoopback.PostAsync(http, ToolBridgeProtocol.CancelEndpoint, first, """{"call_id":"c1"}""");
        await call;
        Assert.Equal(
            (sameSession ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable, 1),
            (refused.StatusCode, tool.Calls.Count));
    }
}
