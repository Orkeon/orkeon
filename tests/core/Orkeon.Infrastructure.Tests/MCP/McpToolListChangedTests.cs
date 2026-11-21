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

        /// <summary>The <c>inputSchema</c> a tool is listed with, as JSON; none when absent.</summary>
        public Dictionary<string, string> Schemas { get; } = [];

        /// <summary>Awaited by a tools/list call (1-based number of the call) after it took its snapshot of the tools.</summary>
        public Func<int, CancellationToken, Task>? ListGate { get; set; }

        /// <summary>Makes a tools/list call (1-based number) answer a JSON-RPC error.</summary>
        public Func<int, bool>? FailList { get; set; }

        /// <summary>Makes a tools/list call (1-based number) answer a result whose <c>tools</c> is no array.</summary>
        public Func<int, bool>? MisshapeList { get; set; }

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
                    if (MisshapeList?.Invoke(call) == true)
                        return new JsonRpcResponse { Id = request.Id, Result = JsonElement.Parse("""{"tools":"none"}""") };
                    var result = new McpToolListResult
                    {
                        Tools = snapshot
                            .Select(n => new McpToolDefinition
                            {
                                Name = n,
                                Description = Descriptions.GetValueOrDefault(n, n),
                                InputSchema = Schemas.TryGetValue(n, out var schema) ? JsonElement.Parse(schema) : null,
                            })
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
        await registry.RegisterToolAsync(builtIn, TestContext.Current.CancellationToken);
        var clock = new ManualTimeProvider();
        var server = new Server("a");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);

        server.Tools = ["a", "shared"];
        await NotifyAsync(server, clock);

        await EventuallyAsync(() => provider.GetServerStatuses().Single().RejectedToolNames.Contains("shared"));
        Assert.Same(builtIn, await registry.GetToolByNameAsync("shared", TestContext.Current.CancellationToken));
        await provider.DisconnectServerAsync("srv");
        Assert.Same(builtIn, await registry.GetToolByNameAsync("shared", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldKeepTheInstanceThatHoldsTheName_WhenTheServerDropsAndListsAgainABridgedTool()
    {
        // Arrange: a tool bridge put its proxy over the adapter of b.
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b");
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);
        var adapter = (await registry.GetToolByNameAsync("b", TestContext.Current.CancellationToken))!;
        var proxy = new Orkeon.Tests.Shared.Doubles.StubWrapperTool(adapter);
        Assert.True(await registry.ReplaceToolAsync(adapter, proxy, TestContext.Current.CancellationToken));

        // Act
        server.Tools = ["a"];
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 1);
        server.Tools = ["a", "b"];
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 2);

        // Assert
        Assert.Same(proxy, await registry.GetToolByNameAsync("b", TestContext.Current.CancellationToken));
        await provider.DisconnectServerAsync("srv");
        Assert.Same(proxy, await registry.GetToolByNameAsync("b", TestContext.Current.CancellationToken));
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
        var held = (await registry.GetToolByNameAsync("b", TestContext.Current.CancellationToken))!;

        server.Tools = ["a"];
        await NotifyAsync(server, clock);
        await EventuallyAsync(async () => await registry.GetToolByNameAsync("b") is null);

        // The task still holds a live instance; the server answers a call to the dropped tool with an error,
        // which reaches the task as a failed call, not as a crash.
        var response = await held.CallAsync(new ToolCallRequest("b", []), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
        Assert.Contains("Unknown tool", response.Error, StringComparison.Ordinal);
        Assert.NotNull(await registry.GetToolByNameAsync("a", TestContext.Current.CancellationToken));
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

    /// <summary>A listing that cannot be read is no empty listing: it takes no tool away, as a failed one takes none.</summary>
    [Fact]
    public async Task ShouldKeepTheRegisteredTools_WhenTheRelistCannotBeRead_AndRecoverOnTheNextNotification()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b") { MisshapeList = call => call == 2 };
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);

        server.Tools = ["a"];
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 1);

        Assert.Equal(["a", "b"], await NamesAsync(registry));

        await NotifyAsync(server, clock);
        await SettledAsync(provider, 2);

        Assert.Equal(["a"], await NamesAsync(registry));
    }

    [Fact]
    public async Task ShouldFailTheConnectionNamingTheServer_WhenTheFirstListingCannotBeRead()
    {
        var registry = new MockToolRegistry();
        var server = new Server("a") { MisshapeList = call => call == 1 };
        await using var provider = new McpToolProvider(registry);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken));

        Assert.Contains("MCP server 'srv'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'tools' is no array", ex.Message, StringComparison.Ordinal);
        Assert.Empty(provider.GetServerStatuses());
        Assert.Empty(await NamesAsync(registry));
    }

    [Fact]
    public async Task ShouldKeepWatching_WhenARelistingOutlivesConnectTimeout()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "b")
        {
            // The second listing never answers: ConnectTimeout cancels it.
            ListGate = (call, ct) => call == 2 ? Task.Delay(Timeout.InfiniteTimeSpan, ct) : Task.CompletedTask,
        };
        await using var provider = new McpToolProvider(registry, timeProvider: clock);
        await provider.ConnectServerAsync(
            "srv", server.Transport, new McpServerConfig { ConnectTimeout = TimeSpan.FromSeconds(1) }, TestContext.Current.CancellationToken);

        server.Tools = ["a"];
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 1);
        Assert.Equal(["a", "b"], await NamesAsync(registry));

        // The watch survived the timed-out listing: the next notification is answered.
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
        var before = await registry.GetToolByNameAsync("a", TestContext.Current.CancellationToken);
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

        var after = await registry.GetToolByNameAsync("a", TestContext.Current.CancellationToken);
        Assert.NotSame(before, after);
        Assert.Empty(gaps);
        Assert.Equal(1, registry.ReplaceToolCallCount);
    }

    [Fact]
    public async Task ShouldSetASchemaThatExpandsBeyondTheBudgetAside_AndWarnOnceAcrossTheListings()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("bomb", "ok");
        server.Schemas["bomb"] = McpToolAdapterRemoteSchemaTests.RefBomb();
        using var logs = new Orkeon.Tests.Shared.Doubles.RecordingLoggerFactory();
        await using var provider = new McpToolProvider(registry, logs, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);

        server.Tools = ["bomb", "ok", "new"];
        await NotifyAsync(server, clock);
        await EventuallyAsync(async () => (await NamesAsync(registry)).SequenceEqual(["new", "ok"]));

        // The bomb was listed and read again at the second listing, and warned about once.
        Assert.Equal(2, server.ListCalls);
        var warning = Assert.Single(logs.Logger.Entries, e => e.EventId.Name == "mcp.tool.unreadable");
        Assert.Contains("bomb", warning.Message, StringComparison.Ordinal);
        Assert.Contains($"beyond {McpToolAdapter.MaxSchemaNodes} schemas", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSetASchemaThatCopiesTooMuchTextAside_ReadItAtEachListing_AndWarnOncePerDefinition()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("bomb", "ok");
        server.Schemas["bomb"] = McpToolAdapterRemoteSchemaTests.TextBomb();
        using var logs = new Orkeon.Tests.Shared.Doubles.RecordingLoggerFactory();
        await using var provider = new McpToolProvider(registry, logs, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);
        int Warnings() => logs.Logger.Entries.Count(e => e.EventId.Name == "mcp.tool.unreadable");

        Assert.Equal(["ok"], await NamesAsync(registry));
        var warning = Assert.Single(logs.Logger.Entries, e => e.EventId.Name == "mcp.tool.unreadable");
        Assert.Contains($"beyond {McpToolAdapter.MaxSchemaTextBytes} bytes of text", warning.Message, StringComparison.Ordinal);

        // The same bomb at the second listing: listed and read again, and not warned about again.
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 1);
        Assert.Equal(2, server.ListCalls);
        Assert.Equal(1, Warnings());
        Assert.Equal(["ok"], await NamesAsync(registry));

        // Another bomb under the same name: read again, and warned about once more.
        server.Schemas["bomb"] = McpToolAdapterRemoteSchemaTests.TextBomb(salt: "changed");
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 2);
        Assert.Equal(2, Warnings());
        Assert.Equal(["ok"], await NamesAsync(registry));
    }

    [Fact]
    public async Task ShouldUnregisterAHeldToolThatBecomesUnreadable_AndRegisterItAgainOnceReadable()
    {
        var registry = new MockToolRegistry();
        var clock = new ManualTimeProvider();
        var server = new Server("a", "ok");
        using var logs = new Orkeon.Tests.Shared.Doubles.RecordingLoggerFactory();
        await using var provider = new McpToolProvider(registry, logs, timeProvider: clock);
        await provider.ConnectServerAsync("srv", server.Transport, new McpServerConfig(), TestContext.Current.CancellationToken);
        Assert.Equal(["a", "ok"], await NamesAsync(registry));

        server.Schemas["a"] = McpToolAdapterRemoteSchemaTests.TextBomb();
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 1);
        Assert.Equal(["ok"], await NamesAsync(registry));
        var warning = Assert.Single(logs.Logger.Entries, e => e.EventId.Name == "mcp.tool.unreadable");
        Assert.Contains("bytes of text", warning.Message, StringComparison.Ordinal);

        server.Schemas["a"] = """{"type":"object","properties":{"q":{"type":"string"}}}""";
        await NotifyAsync(server, clock);
        await SettledAsync(provider, 2);
        Assert.Equal(["a", "ok"], await NamesAsync(registry));
        var tool = (await registry.GetToolByNameAsync("a", TestContext.Current.CancellationToken))!;
        Assert.Equal("string", tool.Schema.Parameters["q"].Type);
    }
}
