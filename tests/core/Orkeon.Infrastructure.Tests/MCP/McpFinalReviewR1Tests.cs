using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.MCP.Auth;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>The final review of FWK, MCP batch R1: M1 to M6 and m7 to m11.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Short-lived doubles whose disposal changes nothing; the transports and servers are disposed.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "The serving task is awaited before the test ends.")]
public sealed class McpFinalReviewR1Tests
{
    private const string Canary = "bearer-canary-5d1e-do-not-leak";
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private int _nextId;

    private JsonRpcRequest Request(string method) =>
        new() { Id = JsonSerializer.SerializeToElement(Interlocked.Increment(ref _nextId)), Method = method };

    private async Task HandshakeAsync(StreamableHttpMcpTransport transport)
    {
        var init = await transport.SendRequestAsync(Request("initialize"), Ct);
        Assert.Null(init.Error);
        await transport.SendNotificationAsync(new JsonRpcNotification { Method = "notifications/initialized" }, Ct);
    }

    // M6: the priming event.

    [Fact]
    public async Task ShouldSkipThePrimingEvent_WhenAnSseAnswerOpensWithAnIdAndNoData()
    {
        await using var server = new FakeStreamableHttpServer("a") { SseAnswers = true };
        server.FramesBeforeToolsList.Add("id: 0\ndata:\n\n");
        using var http = new HttpClient();
        await using var transport = new StreamableHttpMcpTransport(server.Url, http);
        await transport.ConnectAsync(Ct);

        var answer = await transport.SendRequestAsync(Request("tools/list"), Ct);

        Assert.Null(answer.Error);
        Assert.NotNull(answer.Result);
    }

    // M3: a failed re-initialisation.

    [Fact]
    public async Task ShouldKeepTheStaleSession_AndStartOverLater_WhenTheReinitialisationFailed()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        using var http = new HttpClient();
        await using var transport = new StreamableHttpMcpTransport(server.Url, http);
        await transport.ConnectAsync(Ct);
        await HandshakeAsync(transport);
        server.ExpireSessions();
        server.FailInitialize = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendRequestAsync(Request("tools/list"), Ct));
        Assert.Equal("sess-1", transport.SessionId);

        server.FailInitialize = false;
        var answer = await transport.SendRequestAsync(Request("tools/list"), Ct);

        Assert.Null(answer.Error);
        Assert.Equal("sess-2", transport.SessionId);
        Assert.All(server.Requests.Where(r => r.Method == "initialize"), r => Assert.Null(r.SessionId));
    }

    // M1 and m11: the closing of the session.

    [Fact]
    public async Task ShouldDisposeWithoutThrowing_WhenTheCredentialCannotBeHeldForTheDelete()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        var authenticator = new FakeSignInAuthenticator();
        using var http = new HttpClient();
        var transport = new StreamableHttpMcpTransport(server.Url, http, null, authenticator);
        await transport.ConnectAsync(Ct);
        await HandshakeAsync(transport);
        authenticator.QuietFailure = new McpServerConfigurationException("MCP:Servers:x:Auth:TokenEnv", "the variable is empty");

        await transport.DisposeAsync();

        Assert.Equal(1, authenticator.QuietApplications);
        Assert.DoesNotContain(server.Requests, r => r.Verb == "DELETE");
    }

    [Fact]
    public async Task ShouldNeverSignIn_WhenItClosesTheSession()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        var authenticator = new FakeSignInAuthenticator();
        using var http = new HttpClient();
        var transport = new StreamableHttpMcpTransport(server.Url, http, null, authenticator);
        await transport.ConnectAsync(Ct);
        await HandshakeAsync(transport);
        var applied = authenticator.Applied;

        await transport.DisposeAsync();

        Assert.Equal(applied, authenticator.Applied);
        Assert.Equal(0, authenticator.SignIns);
        Assert.Equal(1, authenticator.QuietApplications);
        Assert.Single(server.Requests, r => r.Verb == "DELETE");
    }

    [Fact]
    public async Task ShouldKeepAStaticBearerTokenOutOfTheErrorsAndTheLogs()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, Authorize = (_, _) => false };
        var config = new McpServerConfig { Transport = McpTransportType.Sse, Url = server.Url };
        config.Auth.Type = McpAuthType.Bearer;
        config.Auth.TokenEnv = "ORKEON_TEST_TOKEN";
        var authenticator = McpRequestAuthenticators.Create("srv", config, name => name == "ORKEON_TEST_TOKEN" ? Canary : null);
        var logs = new FakeLoggerFactory();
        using var http = new HttpClient();
        var transport = new StreamableHttpMcpTransport(server.Url, http, new Logger<StreamableHttpMcpTransport>(logs), authenticator);
        await transport.ConnectAsync(Ct);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendRequestAsync(Request("initialize"), Ct));
        await transport.DisposeAsync();

        Assert.DoesNotContain(Canary, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(Canary, StringComparison.Ordinal));
    }

    // M2: the sign-in is outside the timeouts.

    [Fact]
    public async Task ShouldApplyWhatItHolds_AndNeverSignIn_WhenTheOAuthTokenIsAbsent()
    {
        using var endpoint = new StubTokenEndpointHandler();
        using var tokenHttp = new HttpClient(endpoint, disposeHandler: false);
        var time = new ManualTimeProvider();
        using var authenticator = new OAuthMcpAuthenticator("tracker", new OAuth2Client(tokenHttp, time), new OAuthSettings
        {
            ClientId = "orkeon-agent",
            Flow = OAuthFlow.ClientCredentials,
            TokenEndpoint = new Uri("https://idp.example.test/oauth/token"),
            Scopes = [],
        }, "TRACKER_KEY", _ => "secret", null, time);
        using var before = new HttpRequestMessage();
        using var after = new HttpRequestMessage();

        await authenticator.ApplyWithoutSignInAsync(before, Ct);
        await authenticator.EnsureSignedInAsync(Ct);
        await authenticator.ApplyWithoutSignInAsync(after, Ct);

        Assert.Null(before.Headers.Authorization);
        Assert.Equal(1, endpoint.Count);
        Assert.NotNull(after.Headers.Authorization);
    }

    [Fact]
    public async Task ShouldRunTheSignInWithTheCallersToken_NotTheTimeoutOne()
    {
        var transport = ToolServer.Create("slow");
        var client = new McpClient(transport);
        await client.ConnectAsync(Ct);
        using var caller = new CancellationTokenSource();
        CancellationToken seen = default;
        var adapter = new McpToolAdapter(new McpToolDefinition { Name = "slow" }, client, callTimeout: TimeSpan.FromMinutes(5))
        {
            BeforeCall = token =>
            {
                seen = token;
                return ValueTask.CompletedTask;
            },
        };

        await adapter.CallAsync(new ToolCallRequest("slow", []), caller.Token);

        Assert.Equal(caller.Token, seen);
    }

    // m7: notifications/cancelled.

    [Fact]
    public async Task ShouldTellTheServerTheRequestIsCancelled_WhenTheCallerGivesUp()
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(async (request, ct) =>
        {
            if (request.Method == "tools/call")
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ToolServer.Respond(request, ["slow"]);
        });
        var client = new McpClient(transport);
        await client.ConnectAsync(Ct);
        using var cts = new CancellationTokenSource();
        var call = client.CallToolAsync("slow", null, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        var cancelled = Assert.Single(transport.AllSentNotifications, n => n.Method == "notifications/cancelled");
        var called = transport.AllSentRequests.Single(r => r.Method == "tools/call");
        Assert.Equal(called.Id!.Value.GetRawText(), cancelled.Params!.Value.GetProperty("requestId").GetRawText());
    }

    // m10: headers.

    [Theory]
    [InlineData("Accept")]
    [InlineData("content-type")]
    [InlineData("Mcp-Session-Id")]
    public void ShouldRefuseAStaticHeaderTheTransportOwns(string name)
    {
        var config = new McpServerConfig { Transport = McpTransportType.Sse, Url = new Uri("https://mcp.example.test/") };
        config.Headers[name] = "x";

        var ex = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, _ => null));

        Assert.Equal($"MCP:Servers:srv:Headers:{name}", ex.Key);
    }

    [Theory]
    [InlineData("get_weather", "get_weather")]
    [InlineData("a\r\nX-Evil: 1", "a%0D%0AX-Evil%3A%201")]
    public void ShouldKeepAHeaderValueToOneLine(string value, string expected) =>
        Assert.Equal(expected, StreamableHttpMcpTransport.HeaderSafe(value));

    // M4, M5, m10: the server.

    private static McpServer NewServer(McpServerOptions options, FakeMcpServerExtension? extension, params MockGateTool[] tools)
    {
        var registry = new MockToolRegistry();
        foreach (var tool in tools)
            registry.AddTool(tool.Name, tool);
        return new McpServer(registry, ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), options, null, extension);
    }

    private static JsonRpcRequest Rpc(object id, string method, object? @params = null) =>
        JsonSerializer.Deserialize<JsonRpcRequest>(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params }))!;

    [Fact]
    public async Task ShouldNeitherListNorRunATool_WhenTheAllowListLeavesItOut()
    {
        var options = new McpServerOptions();
        options.AllowedTools.Add("keep");
        var server = NewServer(options, null, new MockGateTool("keep", releaseWhenEntered: 1), new MockGateTool("late", releaseWhenEntered: 1));

        var list = await server.ProcessRequestAsync(Rpc(1, "tools/list"), Ct);
        var call = await server.ProcessRequestAsync(Rpc(2, "tools/call", new { name = "late", arguments = new { } }), Ct);

        var names = list!.Result!.Value.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(["keep"], names);
        Assert.NotNull(call!.Error);
    }

    [Fact]
    public async Task ShouldNotReturnTheRawMessageOfAFailure_ToTheClient()
    {
        var extension = new FakeMcpServerExtension { ThrowOnCall = new InvalidOperationException("secret path /home/ops/.key") };
        var server = NewServer(new McpServerOptions(), extension);

        var answer = await server.ProcessRequestAsync(Rpc(1, "tools/call", new { name = "run_crew_demo", arguments = new { } }), Ct);

        Assert.Equal(JsonRpcErrorCodes.InternalError, answer!.Error!.Code);
        Assert.DoesNotContain("secret", answer.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAnswerAPing_WhileACrewCallWaitsAndTheOnlySlotIsTaken()
    {
        var extension = new FakeMcpServerExtension { WaitForFinish = true };
        var registry = new MockToolRegistry();
        var server = new McpServer(registry, ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(),
            new McpServerOptions { MaxConcurrency = 1 }, null, extension);
        var reader = new FakeLineReader();
        var writer = new FakeLineWriter();
        using var stop = new CancellationTokenSource();
        var run = server.RunStdioAsync(reader, writer, stop.Token);

        reader.Send(McpServerStdioHarness.Call(1, "run_crew_demo"));
        reader.Send(McpServerStdioHarness.Call(2, "run_crew_demo"));
        reader.Send(McpServerStdioHarness.Ping(3));
        await McpServerStdioHarness.WaitAsync(() => writer.Lines.Count >= 1, () => string.Join(" | ", writer.Lines));
        extension.Finish();
        reader.Complete();
        await run.WaitAsync(HangGuard, Ct);

        var ids = writer.Lines.Select(line => JsonElement.Parse(line).GetProperty("id").ToString()).ToList();
        Assert.Equal("3", ids[0]);
        Assert.Equal(3, ids.Count);
    }
}
