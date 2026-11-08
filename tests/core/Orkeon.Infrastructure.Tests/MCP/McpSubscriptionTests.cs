using System.Text.Json.Nodes;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-17: <c>subscriptions/listen</c> on a modern server, <c>resources/subscribe</c> on a legacy one, and what <c>resources/updated</c> invalidates.</summary>
public sealed class McpSubscriptionTests
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);
    private static readonly Uri Resource = new("file:///notes.txt");

    private static async Task EventuallyAsync(
        Func<Task<bool>> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        try
        {
            await Polling.WaitUntilAsync(condition, HangGuard);
        }
        catch (TimeoutException)
        {
            // Reported by the assertion below, with the condition.
        }

        Assert.True(await condition(), $"The condition never held: {expression}");
    }

    private static Task EventuallyAsync(
        Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null) =>
        EventuallyAsync(() => Task.FromResult(condition()), expression);

    private static McpServerConfig Config(FakeStreamableHttpServer server) =>
        new() { Transport = McpTransportType.Sse, Url = server.Url };

    private static async Task<List<string>> NamesAsync(MockToolRegistry registry) =>
        (await registry.GetAllToolsAsync()).Select(t => t.Name).Order().ToList();

    private static int Count(FakeStreamableHttpServer server, string method) =>
        server.Requests.Count(r => r.Method == method);

    [Fact]
    public async Task ShouldOpenSubscriptionsListen_AndResyncTheToolsWhenItSaysTheyChanged()
    {
        await using var server = new FakeStreamableHttpServer("a") { Modern = true };
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await EventuallyAsync(() => Count(server, "subscriptions/listen") == 1);

        server.Tools = ["a", "b"];
        server.PushOnListen("notifications/tools/list_changed");
        await EventuallyAsync(() => clock.PendingTimers > 0);
        clock.Advance(McpToolProvider.ToolListDebounce);

        await EventuallyAsync(async () => (await NamesAsync(registry)).SequenceEqual(["a", "b"]));
        var listen = JsonNode.Parse(server.Requests.First(r => r.Method == "subscriptions/listen").Body)!;
        Assert.True(listen["params"]!["notifications"]!["toolsListChanged"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ShouldOpenTheListenStreamAgain_AndResync_WhenTheServerEndsIt()
    {
        await using var server = new FakeStreamableHttpServer("a") { Modern = true };
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await EventuallyAsync(() => Count(server, "subscriptions/listen") == 1);

        server.EndListen();
        await EventuallyAsync(() => clock.PendingTimers > 0);
        clock.Advance(TimeSpan.FromMinutes(5));
        await EventuallyAsync(() => Count(server, "subscriptions/listen") == 2);

        // Events missed while the stream was down: the tools are listed again. The reopening runs inside
        // the Advance above, and the debounce it arms falls due within the same Advance when the watcher
        // is quick: the listing is then already done, otherwise its timer is pending and the clock fires it.
        await EventuallyAsync(() => clock.PendingTimers > 0 || provider.ToolListActivity("srv").Resyncs >= 1);
        if (provider.ToolListActivity("srv").Resyncs < 1)
            clock.Advance(McpToolProvider.ToolListDebounce);
        await EventuallyAsync(() => provider.ToolListActivity("srv").Resyncs >= 1);
    }

    [Fact]
    public async Task ShouldBackOffLonger_WhenTheListenStreamEndsAtOnceTwiceInARow()
    {
        await using var server = new FakeStreamableHttpServer("a") { Modern = true };
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await EventuallyAsync(() => Count(server, "subscriptions/listen") == 1);

        // The clock does not move: the stream lived no time at all, so its end is a failure like any other.
        server.EndListen();
        await EventuallyAsync(() => clock.PendingTimers > 0);
        Assert.Contains(StreamableHttpMcpTransport.Backoff(1), clock.DueIns);
        clock.Advance(StreamableHttpMcpTransport.Backoff(1));
        await EventuallyAsync(() => Count(server, "subscriptions/listen") == 2);

        server.EndListen();
        await EventuallyAsync(() => clock.DueIns.Contains(StreamableHttpMcpTransport.Backoff(2)));
    }

    [Fact]
    public async Task ShouldSubscribeToAResource_AndServeItsReadsFromTheCacheUntilItIsUpdated()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        var registry = new MockToolRegistry();
        await using var provider = new McpToolProvider(registry, timeProvider: new ManualTimeProvider());
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await EventuallyAsync(() => server.GetCount >= 1);
        await provider.SubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        var first = await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        var second = await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        Assert.Equal(1, Count(server, "resources/subscribe"));
        Assert.Equal(1, server.ReadCount);
        Assert.Equal("read-1", first.Contents[0].Text);
        Assert.Equal("read-1", second.Contents[0].Text);

        server.PushOnGet("notifications/resources/updated", "1", new JsonObject { ["uri"] = Resource.ToString() });
        await EventuallyAsync(() => provider.ResourceUpdates("srv") == 1);
        var third = await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        Assert.Equal("read-2", third.Contents[0].Text);
    }

    [Fact]
    public async Task ShouldUnsubscribe_AndStopCachingTheResource()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        await using var provider = new McpToolProvider(new MockToolRegistry(), timeProvider: new ManualTimeProvider());
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await provider.SubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        Assert.Equal(1, server.ReadCount);

        await provider.UnsubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        Assert.Equal(1, Count(server, "resources/unsubscribe"));
        Assert.Equal(3, server.ReadCount);
    }

    [Fact]
    public async Task ShouldDropTheCacheAndSubscribeAgain_WhenTheGetStreamIsReopened()
    {
        var clock = new ManualTimeProvider();
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        await using var provider = new McpToolProvider(new MockToolRegistry(), timeProvider: clock);
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await EventuallyAsync(() => server.GetCount >= 1);
        await provider.SubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        Assert.Equal(1, Count(server, "resources/subscribe"));

        server.DropGet();
        await EventuallyAsync(() => clock.PendingTimers > 0);
        clock.Advance(TimeSpan.FromMinutes(5));

        // What the server said while the stream was down is lost: the read is not served from the cache any more,
        // and the subscription is sent again.
        await EventuallyAsync(() => Count(server, "resources/subscribe") == 2);
        var again = await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        Assert.Equal("read-2", again.Contents[0].Text);
    }

    [Fact]
    public async Task ShouldDropTheCacheAndSubscribeAgain_WhenTheSessionIsInitialisedAgain()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        await using var provider = new McpToolProvider(new MockToolRegistry(), timeProvider: new ManualTimeProvider());
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await provider.SubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        server.ExpireSessions();
        await provider.ListPromptsAsync("srv", TestContext.Current.CancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);

        await EventuallyAsync(() => Count(server, "resources/subscribe") == 2);
        Assert.Equal("sess-2", server.Requests.Last(r => r.Method == "resources/subscribe").SessionId);
        var again = await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        Assert.Equal("read-2", again.Contents[0].Text);
    }

    [Fact]
    public async Task ShouldNotCacheARead_ThatWasInFlightWhenTheResourceWasUpdated()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true, ReadGate = gate };
        await using var provider = new McpToolProvider(new MockToolRegistry(), timeProvider: new ManualTimeProvider());
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await EventuallyAsync(() => server.GetCount >= 1);
        await provider.SubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        var inFlight = provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        await EventuallyAsync(() => Count(server, "resources/read") == 1);
        server.PushOnGet("notifications/resources/updated", "1", new JsonObject { ["uri"] = Resource.ToString() });
        await EventuallyAsync(() => provider.ResourceUpdates("srv") == 1);
        gate.SetResult();
        await inFlight;

        // The value that was read before the update must not stay in the cache.
        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        Assert.Equal(2, server.ReadCount);
    }

    [Fact]
    public async Task ShouldNotRelistTheTools_WhenAResourceIsUpdated()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);
        await EventuallyAsync(() => server.GetCount >= 1);
        await provider.SubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        server.PushOnGet("notifications/resources/updated", "1", new JsonObject { ["uri"] = Resource.ToString() });
        await EventuallyAsync(() => provider.ResourceUpdates("srv") == 1);

        Assert.Equal(0, provider.ToolListActivity("srv").Notifications);
        Assert.Equal(0, clock.PendingTimers);
        Assert.Equal(1, Count(server, "tools/list"));
    }

    [Fact]
    public async Task ShouldNotCacheAResourceItDidNotSubscribeTo()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        await using var provider = new McpToolProvider(new MockToolRegistry(), timeProvider: new ManualTimeProvider());
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);

        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);
        await provider.ReadResourceAsync("srv", Resource, TestContext.Current.CancellationToken);

        Assert.Equal(2, server.ReadCount);
    }

    [Fact]
    public async Task ShouldRefuseToSubscribe_WhenTheServerDoesNotAnnounceIt()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, ResourceSubscribe = false };
        await using var provider = new McpToolProvider(new MockToolRegistry(), timeProvider: new ManualTimeProvider());
        await provider.ConnectServerAsync("srv", Config(server), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SubscribeResourceAsync("srv", Resource, TestContext.Current.CancellationToken));

        Assert.Contains("subscribe", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Count(server, "resources/subscribe"));
    }
}
