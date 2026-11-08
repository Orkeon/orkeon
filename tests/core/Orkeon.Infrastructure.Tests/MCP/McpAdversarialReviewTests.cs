using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.MCP.Auth;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>The adversarial review of the FWK pull request, MCP client, transports and auth (MCP-2, 3, 4, 6, 8, 9, 10, X-9).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Short-lived doubles whose disposal changes nothing; the transports and servers are disposed.")]
public sealed class McpAdversarialReviewTests
{
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

    private static string? Environment(string name) => name is "MCP_TOKEN" or "MCP_KEY" ? "secret-value" : null;

    // MCP-3: the server's own address carries the credential.

    [Theory]
    [InlineData("http://mcp.example.com/mcp", true)]
    [InlineData("http://127.0.0.1:9000/mcp", false)]
    [InlineData("https://mcp.example.com/mcp", false)]
    public void ShouldRefuseAPlainHttpServerUrl_WhenABearerIsAttached(string url, bool refused)
    {
        var config = new McpServerConfig
        {
            Transport = McpTransportType.Sse,
            Url = new Uri(url),
            Auth = { Type = McpAuthType.Bearer, TokenEnv = "MCP_TOKEN" },
        };

        var error = Record.Exception(() => (McpRequestAuthenticators.Create("srv", config, Environment) as IDisposable)?.Dispose());

        if (refused)
            Assert.Equal("MCP:Servers:srv:Url", Assert.IsType<McpServerConfigurationException>(error).Key);
        else
            Assert.Null(error);
    }

    [Fact]
    public void ShouldRefuseAPlainHttpServerUrl_WhenAHeaderIsReadFromTheEnvironment()
    {
        var config = new McpServerConfig { Transport = McpTransportType.Sse, Url = new Uri("http://mcp.example.com/mcp") };
        config.Headers["X-Api-Key"] = "env:MCP_KEY";

        var error = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, Environment));

        Assert.Equal("MCP:Servers:srv:Url", error.Key);
    }

    [Fact]
    public void ShouldAcceptAPlainHttpServerUrl_WhenNoCredentialIsAttached()
    {
        var config = new McpServerConfig { Transport = McpTransportType.Sse, Url = new Uri("http://mcp.example.com/mcp") };
        config.Headers["X-Tenant"] = "acme";

        Assert.NotNull(McpRequestAuthenticators.Create("srv", config, Environment));
    }

    // MCP-9: the headers the transport owns.

    [Theory]
    [InlineData("MCP-Protocol-Version")]
    [InlineData("Mcp-Method")]
    [InlineData("Mcp-Name")]
    [InlineData("Last-Event-ID")]
    public void ShouldRefuseAStaticHeader_ThatTheTransportSets(string name)
    {
        var config = new McpServerConfig { Transport = McpTransportType.Sse, Url = new Uri("https://mcp.example.com/mcp") };
        config.Headers[name] = "fixed";

        var error = Assert.Throws<McpServerConfigurationException>(() => McpRequestAuthenticators.Create("srv", config, Environment));

        Assert.Equal($"MCP:Servers:srv:Headers:{name}", error.Key);
    }

    [Fact]
    public async Task ShouldNotOverwriteAHeaderTheRequestAlreadyCarries_WithAStaticValue()
    {
        var authenticator = new StaticHeaderAuthenticator(new Dictionary<string, string> { ["X-Trace"] = "static", ["X-Tenant"] = "acme" });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.example.com/mcp");
        request.Headers.TryAddWithoutValidation("X-Trace", "transport");

        await authenticator.ApplyAsync(request, Ct);

        Assert.Equal("transport", Assert.Single(request.Headers.GetValues("X-Trace")));
        Assert.Equal("acme", Assert.Single(request.Headers.GetValues("X-Tenant")));
    }

    // MCP-2: the session is adopted once its handshake is over.

    [Fact]
    public async Task ShouldKeepTheStaleSession_WhenTheReplayedInitializedIsRefused()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        using var http = new HttpClient();
        await using var transport = new StreamableHttpMcpTransport(server.Url, http);
        await transport.ConnectAsync(Ct);
        await HandshakeAsync(transport);
        server.ExpireSessions();
        server.FailInitialized = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendRequestAsync(Request("tools/list"), Ct));
        Assert.Equal("sess-1", transport.SessionId);

        server.FailInitialized = false;
        var answer = await transport.SendRequestAsync(Request("tools/list"), Ct);

        Assert.Null(answer.Error);
        // The session handed out by the failed handshake is never used: a new one was started, and told initialized.
        // (The GET stream may start a session of its own meanwhile: the id is whichever handshake finished last.)
        var session = transport.SessionId;
        Assert.NotEqual("sess-2", session);
        Assert.Contains(server.Requests, r => r.Method == "notifications/initialized" && r.SessionId == session);
        Assert.DoesNotContain(server.Requests, r => r.Method == "tools/list" && r.SessionId == "sess-2");
    }

    [Fact]
    public async Task ShouldNotSendInitialized_WhenTheReplayedInitializeAnswersAnError()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true };
        using var http = new HttpClient();
        await using var transport = new StreamableHttpMcpTransport(server.Url, http);
        await transport.ConnectAsync(Ct);
        await HandshakeAsync(transport);
        server.ExpireSessions();
        server.InitializeError = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendRequestAsync(Request("tools/list"), Ct));

        Assert.Equal("sess-1", transport.SessionId);
        Assert.Single(server.Requests, r => r.Method == "notifications/initialized");
    }

    [Fact]
    public async Task ShouldStartTheSessionAgain_WhenTheReplayedInitializeIsAnsweredAsAnSseStream()
    {
        await using var server = new FakeStreamableHttpServer("a") { UseSessions = true, SseAnswers = true };
        using var http = new HttpClient();
        await using var transport = new StreamableHttpMcpTransport(server.Url, http);
        await transport.ConnectAsync(Ct);
        await HandshakeAsync(transport);
        server.ExpireSessions();

        var answer = await transport.SendRequestAsync(Request("tools/list"), Ct);

        Assert.Null(answer.Error);
        var session = transport.SessionId;
        Assert.NotEqual("sess-1", session);
        Assert.Contains(server.Requests, r => r.Method == "notifications/initialized" && r.SessionId == session);
    }

    // MCP-8: bounded reads.

    [Fact]
    public async Task ShouldRefuseAnEvent_WhoseDataLinesAddUpPastTheBound()
    {
        var body = string.Concat(Enumerable.Repeat("data: abcdefghij\n", 10));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in SseEventReader.ReadAsync(stream, 32, Ct))
            {
            }
        });
    }

    [Fact]
    public async Task ShouldRefuseAJsonBody_LongerThanTheBound()
    {
        using var announced = new StringContent(new string('x', 100));
        using var chunked = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100))));

        await Assert.ThrowsAsync<InvalidDataException>(() => StreamableHttpMcpTransport.ReadBodyAsync(announced, Ct, max: 10));
        await Assert.ThrowsAsync<InvalidDataException>(() => StreamableHttpMcpTransport.ReadBodyAsync(chunked, Ct, max: 10));
        Assert.Equal(new string('x', 100), await StreamableHttpMcpTransport.ReadBodyAsync(new StringContent(new string('x', 100)), Ct, max: 100));
    }

    // MCP-10: the transport's own client follows no redirect.

    [Fact]
    public async Task ShouldNotFollowARedirect_WhenTheTransportOwnsItsClient()
    {
        var (listener, prefix) = LoopbackPorts.StartListener("/redirect/");
        var paths = new List<string>();
        var serving = Task.Run(async () =>
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                lock (paths)
                    paths.Add(context.Request.Url!.AbsolutePath);
                if (context.Request.Url!.AbsolutePath.EndsWith("/mcp", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 307;
                    context.Response.RedirectLocation = new Uri(prefix, "other").ToString();
                }
                else
                {
                    var bytes = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"result":{}}""");
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(bytes, Ct);
                }

                context.Response.Close();
            }
        }, Ct);

        try
        {
            await using var transport = new StreamableHttpMcpTransport(new Uri(prefix, "mcp"));
            await transport.ConnectAsync(Ct);

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendRequestAsync(Request("tools/list"), Ct));

            Assert.Equal(HttpStatusCode.TemporaryRedirect, error.StatusCode);
            lock (paths)
                Assert.DoesNotContain(paths, p => p.EndsWith("/other", StringComparison.Ordinal));
        }
        finally
        {
            listener.Close();
            await serving.WaitAsync(HangGuard, Ct);
        }
    }

    // MCP-6: a prompt or a resource is bounded as a tool call is.

    [Fact]
    public async Task ShouldTimeOutAPromptGet_WhenTheServerStaysSilentPastCallTimeout()
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(async (request, ct) =>
        {
            if (request.Method is "prompts/get" or "prompts/list")
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ToolServer.Respond(request, ["a"]);
        });
        await using var provider = new McpToolProvider(new MockToolRegistry());
        await provider.ConnectServerAsync("srv", transport, new McpServerConfig { CallTimeout = TimeSpan.FromMilliseconds(100) }, Ct);

        var get = await Assert.ThrowsAsync<TimeoutException>(
            () => provider.GetPromptAsync("srv", "greet", null, Ct).WaitAsync(HangGuard, Ct));
        var list = await Assert.ThrowsAsync<TimeoutException>(
            () => provider.ListPromptsAsync("srv", Ct).WaitAsync(HangGuard, Ct));

        Assert.Contains("MCP:Servers:srv:CallTimeout", get.Message, StringComparison.Ordinal);
        Assert.Contains("prompts/list", list.Message, StringComparison.Ordinal);
    }

    // X-9: the authenticator is disposed with its transport.

    [Fact]
    public async Task ShouldDisposeItsAuthenticator_WhenTheTransportIsDisposed()
    {
        var authenticator = new StubDisposableRequestAuthenticator();
        var transport = new StreamableHttpMcpTransport(new Uri("https://mcp.example.com/mcp"), new HttpClient(), authenticator: authenticator);
        await transport.ConnectAsync(Ct);

        await transport.DisposeAsync();

        Assert.True(authenticator.Disposed);
    }

    [Fact]
    public void ShouldDisposeEveryLink_WhenAChainIsDisposed()
    {
        var first = new StubDisposableRequestAuthenticator();
        var second = new StubDisposableRequestAuthenticator();

        new ChainedMcpRequestAuthenticator([first, second]).Dispose();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    // MCP-4: the server lets queued extension calls go when its input ends.

    [Fact]
    public async Task ShouldTellTheExtensionToStopAdmitting_WhenStdinCloses()
    {
        var extension = new FakeMcpServerExtension();
        var server = new McpServer(new MockToolRegistry(), ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(),
            new McpServerOptions(), null, extension);
        var reader = new FakeLineReader();
        var writer = new FakeLineWriter();
        var run = server.RunStdioAsync(reader, writer, Ct);
        Assert.False(extension.StoppedAdmitting);

        reader.Complete();
        await run.WaitAsync(HangGuard, Ct);

        Assert.True(extension.StoppedAdmitting);
    }
}
