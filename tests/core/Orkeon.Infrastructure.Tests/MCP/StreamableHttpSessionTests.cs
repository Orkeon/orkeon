using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-17: a real Streamable HTTP session: session id, 404 re-initialisation, streamed answers, the GET stream and its resumption.</summary>
public sealed class StreamableHttpSessionTests
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);
    private int _nextId;

    private JsonRpcRequest Request(string method) =>
        new() { Id = JsonSerializer.SerializeToElement(Interlocked.Increment(ref _nextId)), Method = method };

    private static async Task EventuallyAsync(
        Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        try
        {
            await Polling.WaitUntilAsync(() => Task.FromResult(condition()), HangGuard);
        }
        catch (TimeoutException)
        {
            // Reported by the assertion below, with the condition.
        }

        Assert.True(condition(), $"The condition never held: {expression}");
    }

    private static async Task<JsonRpcNotification> NextNotificationAsync(StreamableHttpMcpTransport transport)
    {
        using var guard = new CancellationTokenSource(HangGuard);
        try
        {
            return await transport.Notifications.ReadAsync(guard.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("No notification reached the transport.");
            throw;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The test disposes both.")]
    private static async Task<(StreamableHttpMcpTransport Transport, HttpClient Http)> OpenAsync(
        FakeStreamableHttpServer server, TimeProvider? clock = null)
    {
        var http = new HttpClient();
        var transport = new StreamableHttpMcpTransport(server.Url, http, null, null, clock);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        return (transport, http);
    }

    private static async Task HandshakeAsync(StreamableHttpMcpTransport transport, Func<string, JsonRpcRequest> request)
    {
        var init = await transport.SendRequestAsync(request("initialize"), TestContext.Current.CancellationToken);
        Assert.Null(init.Error);
        await transport.SendNotificationAsync(
            new JsonRpcNotification { Method = "notifications/initialized" }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldCaptureTheSessionId_AndSendItOnTheNextMessages()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;

        await HandshakeAsync(transport, Request);
        var list = await transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken);

        Assert.Null(list.Error);
        Assert.Equal("sess-1", transport.SessionId);
        var seen = server.Requests;
        Assert.Null(seen.Single(r => r.Method == "initialize").SessionId);
        Assert.Equal("sess-1", seen.Single(r => r.Method == "tools/list").SessionId);
        Assert.Equal("sess-1", seen.Single(r => r.Method == "notifications/initialized").SessionId);
    }

    [Fact]
    public async Task ShouldSendTheNegotiatedVersion_OnceTheSessionIsInitialised()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;

        await HandshakeAsync(transport, Request);
        await transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken);

        Assert.Equal("2025-06-18", server.Requests.Single(r => r.Method == "tools/list").ProtocolVersion);
    }

    [Fact]
    public async Task ShouldInitialiseAgain_WhenTheServerForgotTheSession()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);
        server.ExpireSessions();

        var list = await transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken);

        Assert.Null(list.Error);
        Assert.Equal("sess-2", transport.SessionId);
        var seen = server.Requests;
        Assert.Equal(2, seen.Count(r => r.Method == "initialize"));
        Assert.Equal("sess-2", seen.Last(r => r.Method == "tools/list").SessionId);
    }

    [Fact]
    public async Task ShouldHandOverNotificationsOfAnSseAnswer_BeforeItsFinalMessage()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeStreamableHttpServer("a")
        {
            SseAnswers = true,
            ToolsListGate = gate,
        };
        server.FramesBeforeToolsList.Add(
            "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n");
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);

        var pending = transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken);
        var notification = await NextNotificationAsync(transport);

        Assert.Equal("notifications/tools/list_changed", notification.Method);
        Assert.False(pending.IsCompleted);
        gate.SetResult();
        var response = await pending;
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task ShouldAnswerAServerRequestOfAStream_WithMethodNotFound()
    {
        await using var server = new FakeStreamableHttpServer("a") { SseAnswers = true };
        server.FramesBeforeToolsList.Add(
            "data: {\"jsonrpc\":\"2.0\",\"id\":\"srv-1\",\"method\":\"sampling/createMessage\"}\n\n");
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);

        var list = await transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken);

        Assert.Null(list.Error);
        await EventuallyAsync(() => server.Requests.Any(r => r.Verb == "POST" && r.Method is null && r.Body.Contains("srv-1", StringComparison.Ordinal)));
        var reply = JsonNode.Parse(server.Requests.First(r => r.Method is null && r.Verb == "POST").Body)!;
        Assert.Equal(-32601, reply["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task ShouldHearTheGetStream_OnceTheSessionIsInitialised()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);
        await EventuallyAsync(() => server.GetCount >= 1);

        server.PushOnGet("notifications/tools/list_changed", "1");
        var notification = await NextNotificationAsync(transport);

        Assert.Equal("notifications/tools/list_changed", notification.Method);
        var get = server.Requests.First(r => r.Verb == "GET");
        Assert.Equal("sess-1", get.SessionId);
    }

    [Fact]
    public async Task ShouldReconnectTheGetStream_WithTheLastEventId()
    {
        var clock = new ManualTimeProvider();
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        var (transport, http) = await OpenAsync(server, clock);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);
        await EventuallyAsync(() => server.GetCount >= 1);
        server.PushOnGet("notifications/tools/list_changed", "41");
        await NextNotificationAsync(transport);

        server.DropGet();
        await EventuallyAsync(() => clock.PendingTimers > 0);
        clock.Advance(TimeSpan.FromMinutes(5));
        await EventuallyAsync(() => server.GetCount >= 2);

        var second = server.Requests.Where(r => r.Verb == "GET").ElementAt(1);
        Assert.Equal("41", second.LastEventId);
        server.PushOnGet("notifications/prompts/list_changed", "42");
        var next = await NextNotificationAsync(transport);
        Assert.Equal("notifications/prompts/list_changed", next.Method);
    }

    [Fact]
    public async Task ShouldReconnectTheGetStreamWithoutALastEventId_WhenAnEmptyIdResetIt()
    {
        var clock = new ManualTimeProvider();
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        var (transport, http) = await OpenAsync(server, clock);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);
        await EventuallyAsync(() => server.GetCount >= 1);
        server.PushOnGet("notifications/tools/list_changed", "41");
        await NextNotificationAsync(transport);
        // An empty id: field resets the last event id (SSE specification).
        server.PushOnGet("notifications/prompts/list_changed", "");
        await NextNotificationAsync(transport);

        server.DropGet();
        await EventuallyAsync(() => clock.PendingTimers > 0);
        clock.Advance(TimeSpan.FromMinutes(5));
        await EventuallyAsync(() => server.GetCount >= 2);

        Assert.Null(server.Requests.Where(r => r.Verb == "GET").ElementAt(1).LastEventId);
    }

    [Fact]
    public async Task ShouldNotRetryTheGetStream_WhenTheServerAnswers405()
    {
        var clock = new ManualTimeProvider();
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = false };
        var (transport, http) = await OpenAsync(server, clock);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);
        await EventuallyAsync(() => server.GetCount >= 1);

        var list = await transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken);

        Assert.Null(list.Error);
        Assert.Equal(1, server.GetCount);
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task ShouldAnswerAPingOfTheServer_WithAnEmptyResult_AndOtherRequestsWithMethodNotFound()
    {
        await using var server = new FakeStreamableHttpServer("a") { SseAnswers = true };
        server.FramesBeforeToolsList.Add("data: {\"jsonrpc\":\"2.0\",\"id\":\"ping-1\",\"method\":\"ping\"}\n\n");
        server.FramesBeforeToolsList.Add("data: {\"jsonrpc\":\"2.0\",\"id\":\"srv-2\",\"method\":\"roots/list\"}\n\n");
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);

        await transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken);

        await EventuallyAsync(() => server.Requests.Count(r => r.Verb == "POST" && r.Method is null) == 2);
        var replies = server.Requests.Where(r => r.Verb == "POST" && r.Method is null)
            .Select(r => JsonNode.Parse(r.Body)!).ToList();
        var ping = replies.Single(r => r["id"]!.GetValue<string>() == "ping-1");
        Assert.Null(ping["error"]);
        Assert.NotNull(ping["result"]);
        var roots = replies.Single(r => r["id"]!.GetValue<string>() == "srv-2");
        Assert.Equal(-32601, roots["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task ShouldAnswerAPingOnTheGetStream_WithAnEmptyResult()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);
        await EventuallyAsync(() => server.GetCount >= 1);

        server.PushRawOnGet("{\"jsonrpc\":\"2.0\",\"id\":\"ping-9\",\"method\":\"ping\"}", "1");

        await EventuallyAsync(() => server.Requests.Any(r => r.Verb == "POST" && r.Method is null && r.Body.Contains("ping-9", StringComparison.Ordinal)));
        var reply = JsonNode.Parse(server.Requests.First(r => r.Method is null && r.Verb == "POST").Body)!;
        Assert.Null(reply["error"]);
        Assert.NotNull(reply["result"]);
    }

    [Fact]
    public async Task ShouldBackOffAndGiveUp_WhenEveryGetOfASessionIsAnswered404()
    {
        var clock = new ManualTimeProvider();
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, AllowGet = true, GetNotFound = true };
        var (transport, http) = await OpenAsync(server, clock);
        using var _ = http;
        await using var __ = transport;

        await HandshakeAsync(transport, Request);
        for (var i = 0; i < StreamableHttpMcpTransport.MaxConsecutiveReinitializations; i++)
        {
            // Each re-initialisation is followed by a wait: never a hot loop.
            // The clock moves by this wait alone: a longer move would also fire the next one, armed meanwhile.
            await EventuallyAsync(() => clock.PendingTimers > 0);
            clock.Advance(Assert.Single(clock.DueIns));
        }

        var cap = StreamableHttpMcpTransport.MaxConsecutiveReinitializations;
        await EventuallyAsync(() => server.GetCount == cap + 1);
        await EventuallyAsync(() => clock.PendingTimers == 0);
        Assert.Equal(1 + cap, server.Requests.Count(r => r.Method == "initialize"));
        Assert.Equal(cap + 1, server.GetCount);
    }

    [Fact]
    public async Task ShouldInitialiseOnce_WhenSeveralRequestsFindTheSessionStaleTogether()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;
        await HandshakeAsync(transport, Request);
        server.ExpireSessions();

        var answers = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => transport.SendRequestAsync(Request("tools/list"), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.All(answers, a => Assert.Null(a.Error));
        Assert.Equal(2, server.Requests.Count(r => r.Method == "initialize"));
        Assert.Equal("sess-2", transport.SessionId);
    }

    [Fact]
    public async Task ShouldEndTheSession_WhenTheTransportIsDisposed()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await HandshakeAsync(transport, Request);

        await transport.DisposeAsync();

        var delete = Assert.Single(server.Requests, r => r.Verb == "DELETE");
        Assert.Equal("sess-1", delete.SessionId);
    }
}
