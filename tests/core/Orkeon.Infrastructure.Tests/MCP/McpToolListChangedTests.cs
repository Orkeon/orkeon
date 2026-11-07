using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-10: notifications/tools/list_changed re-lists after a 500 ms debounce and re-syncs the registry.</summary>
public sealed class McpToolListChangedTests
{
    private const string ListChanged = "notifications/tools/list_changed";

    /// <summary>A fake server whose tool list a test changes; a call to a tool it no longer lists fails as a real server's does.</summary>
    private sealed class Server
    {
        public string[] Tools { get; set; }
        public Dictionary<string, string> Descriptions { get; } = [];

        /// <summary>Awaited by a tools/list call (1-based number of the call) after it took its snapshot of the tools.</summary>
        public Func<int, CancellationToken, Task>? ListGate { get; set; }

        /// <summary>Makes a tools/list call (1-based number) answer a JSON-RPC error.</summary>
        public Func<int, bool>? FailList { get; set; }

        public MockMcpTransport Transport { get; }

        [SuppressMessage("Reliability", "CA2000", Justification = "Ownership of the transport passes to the provider, which the test disposes.")]
        public Server(params string[] tools)
        {
            Tools = tools;
            Transport = new MockMcpTransport();
            Transport.SetConnected(true);
            Transport.SetSendRequestFunc(HandleAsync);
        }

        public int ListCalls => Transport.AllSentRequests.Count(r => r.Method == "tools/list");

        private async Task<JsonRpcResponse> HandleAsync(JsonRpcRequest request, CancellationToken ct)
        {
            var snapshot = Tools;
            switch (request.Method)
            {
                case "tools/list":
                    var call = ListCalls;
                    if (ListGate is not null)
                        await ListGate(call, ct);
                    if (FailList?.Invoke(call) == true)
                        return new JsonRpcResponse { Id = request.Id, Error = new JsonRpcError(-32603, "list failed") };
                    var result = new McpToolListResult
                    {
                        Tools = snapshot
                            .Select(n => new McpToolDefinition { Name = n, Description = Descriptions.GetValueOrDefault(n, n) })
                            .ToList(),
                    };
                    return new JsonRpcResponse { Id = request.Id, Result = JsonSerializer.SerializeToElement(result) };
                case "tools/call":
                    var name = request.Params!.Value.GetProperty("name").GetString()!;
                    return Tools.Contains(name)
                        ? ToolServer.Respond(request, Tools)
                        : new JsonRpcResponse { Id = request.Id, Error = new JsonRpcError(-32602, $"Unknown tool: {name}") };
                default:
                    return ToolServer.Respond(request, Tools);
            }
        }
    }

    /// <summary>
    /// How long a wait may last before it is given up: a hang guard, far above anything a loaded machine
    /// needs, never a bound the code under test is held to. The verdict is the assertion that follows.
    /// </summary>
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);

    /// <summary>Waits for <paramref name="condition"/>, then asserts it: a code that never gets there fails an assertion naming the condition.</summary>
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

    private static async Task<List<string>> NamesAsync(MockToolRegistry registry) =>
        (await registry.GetAllToolsAsync()).Select(t => t.Name).Order().ToList();

    /// <summary>Sends the notification and lets the debounce elapse on the manual clock.</summary>
    private static async Task NotifyAsync(Server server, ManualTimeProvider clock)
    {
        server.Transport.PushNotification(ListChanged);
        await EventuallyAsync(() => clock.PendingTimers > 0);
        clock.Advance(TimeSpan.FromMilliseconds(500));
    }

    private static Task SettledAsync(McpToolProvider provider, long resyncs) =>
        EventuallyAsync(() => provider.ToolListActivity("srv").Resyncs >= resyncs);

    [Fact]
    public async Task ShouldUnregisterAToolTheServerNoLongerLists_AndRegisterANewOne()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);

        server.Tools = ["a", "c"];
        await NotifyAsync(server, clock);

        await EventuallyAsync(async () => (await NamesAsync(registry)).SequenceEqual(["a", "c"]));
    }

    [Fact]
    public async Task ShouldListOnce_WhenSeveralNotificationsArriveWithinTheDebounce()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);
        var before = server.ListCalls;

        server.Transport.PushNotification(ListChanged);
        await EventuallyAsync(() => clock.PendingTimers > 0);
        server.Transport.PushNotification(ListChanged);
        await EventuallyAsync(() => provider.ToolListActivity("srv").Notifications == 2);
        // The second notification falls inside the wait: nothing is listed until the clock reaches it.
        Assert.Equal(before, server.ListCalls);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await SettledAsync(provider, 1);

        Assert.Equal(before + 1, server.ListCalls);
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task ShouldIgnoreACollisionWithABuiltIn_AndKeepTheBuiltIn()
    {
        var registry = new MockToolRegistry();
        var builtIn = new MockTool("shared");
        await registry.RegisterToolAsync(builtIn);
        var clock = new ManualTimeProvider();
        var server = new Server("a");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);

        server.Tools = ["a", "shared"];
        await NotifyAsync(server, clock);

        await EventuallyAsync(() => provider.GetServerStatuses().Single().RejectedToolNames.Contains("shared"));
        Assert.Same(builtIn, await registry.GetToolByNameAsync("shared"));
        await provider.DisconnectServerAsync("srv");
        Assert.Same(builtIn, await registry.GetToolByNameAsync("shared"));
    }

    [Fact]
    public async Task ShouldLeaveARunningTaskItsToolInstance_WhenTheServerDropsIt()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);
        // What a task in progress holds: the tool instance it was given when it started.
        var held = (await registry.GetToolByNameAsync("b"))!;

        server.Tools = ["a"];
        await NotifyAsync(server, clock);
        await EventuallyAsync(async () => await registry.GetToolByNameAsync("b") is null);

        // The task still holds a live instance; the server answers a call to the dropped tool with an error,
        // which reaches the task as a failed call, not as a crash.
        var response = await held.CallAsync(new ToolCallRequest("b", []), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
        Assert.Contains("Unknown tool", response.Error, StringComparison.Ordinal);
        Assert.NotNull(await registry.GetToolByNameAsync("a"));
    }

    [Fact]
    public async Task ShouldNotRegisterANewTool_WhenTheToolsFilterDeniesIt()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a");
        var config = new McpServerConfig();
        config.Tools.Deny.Add("blocked");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, config, TestContext.Current.CancellationToken);

        server.Tools = ["a", "blocked", "fresh"];
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 1);

        Assert.Equal(["a", "fresh"], await NamesAsync(registry));
        Assert.Empty(provider.GetServerStatuses().Single().RejectedToolNames);
    }

    [Fact]
    public async Task ShouldKeepTheRegisteredTools_WhenTheRelistFails_AndRecoverOnTheNextNotification()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b") { FailList = call => call == 2 };
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);

        server.Tools = ["a"];
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 1);

        Assert.Equal(["a", "b"], await NamesAsync(registry));
        Assert.True(provider.GetServerStatuses().Single().IsConnected);

        await NotifyAsync(server, clock);
        await SettledAsync(provider, 2);

        Assert.Equal(["a"], await NamesAsync(registry));
    }

    [Fact]
    public async Task ShouldStopWithoutListing_WhenTheProviderIsDisposedDuringTheDebounce()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a");
        var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);
        var before = server.ListCalls;
        server.Transport.PushNotification(ListChanged);
        await EventuallyAsync(() => clock.PendingTimers > 0);

        await provider.DisposeAsync();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await Task.Yield();

        Assert.Equal(before, server.ListCalls);
        Assert.Empty(await NamesAsync(registry));
        Assert.Equal(0, clock.PendingTimers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldListAgain_WhenTheServerNotifiesDuringARelist(bool floodWithOtherNotifications)
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The first re-list (call 2) answers the snapshot it took, once the test lets it go.
        server.ListGate = (call, ct) => call == 2 ? release.Task.WaitAsync(ct) : Task.CompletedTask;
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);

        await NotifyAsync(server, clock);
        await EventuallyAsync(() => server.ListCalls == 2);
        server.Tools = ["a", "c"];
        server.Transport.PushNotification(ListChanged);
        await EventuallyAsync(() => provider.ToolListActivity("srv").Notifications == 2);
        if (floodWithOtherNotifications)
        {
            // More than the channel holds: the oldest entries are dropped, none of them the one already read.
            for (var i = 0; i < 300; i++)
                server.Transport.PushNotification("notifications/progress");
        }

        release.SetResult();
        await EventuallyAsync(() => clock.PendingTimers > 0);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await SettledAsync(provider, 2);

        Assert.Equal(["a", "c"], await NamesAsync(registry));
    }

    [Fact]
    public async Task ShouldReplaceAChangedTool_WithoutEverLeavingTheNameUnresolved()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);
        var before = await registry.GetToolByNameAsync("a");
        var gaps = new List<string>();
        registry.OnUnregistered = name =>
        {
            if (registry.GetToolByNameAsync(name).Result is null)
                gaps.Add(name);
        };

        server.Descriptions["a"] = "a, now described differently";
        await NotifyAsync(server, clock);
        await EventuallyAsync(
            async () => (await registry.GetToolByNameAsync("a"))?.Description == "a, now described differently");

        var after = await registry.GetToolByNameAsync("a");
        Assert.NotSame(before, after);
        Assert.Empty(gaps);
        Assert.Equal(1, registry.ReplaceToolCallCount);
    }
}
