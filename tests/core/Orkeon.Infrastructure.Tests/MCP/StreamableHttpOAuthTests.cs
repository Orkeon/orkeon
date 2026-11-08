using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.MCP.Auth;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-16: a 401 from the server makes the transport renew the credential and send the request again, once.</summary>
public sealed class StreamableHttpOAuthTests : IDisposable
{
    private const string CredentialVariable = "TRACKER_OAUTH_CLIENT_KEY";

    private readonly StubTokenEndpointHandler _endpoint = new();
    private readonly HttpClient _tokenHttp;
    private readonly ManualTimeProvider _time = new();
    private int _nextId;

    public StreamableHttpOAuthTests() => _tokenHttp = new HttpClient(_endpoint, disposeHandler: false);

    public void Dispose()
    {
        _tokenHttp.Dispose();
        _endpoint.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private JsonRpcRequest Request(string method) =>
        new() { Id = JsonSerializer.SerializeToElement(Interlocked.Increment(ref _nextId)), Method = method };

    private OAuthMcpAuthenticator Authenticator() =>
        new("tracker", new OAuth2Client(_tokenHttp, _time), new OAuthSettings
        {
            ClientId = "orkeon-agent",
            Flow = OAuthFlow.ClientCredentials,
            TokenEndpoint = new Uri("https://idp.example.test/oauth/token"),
            Scopes = [],
        }, CredentialVariable, _ => "canary-7f3a-do-not-leak", null, _time);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The test disposes both.")]
    private async Task<(StreamableHttpMcpTransport Transport, HttpClient Http)> OpenAsync(FakeStreamableHttpServer server)
    {
        var http = new HttpClient();
        var transport = new StreamableHttpMcpTransport(server.Url, http, null, Authenticator(), _time);
        await transport.ConnectAsync(Ct);
        return (transport, http);
    }

    [Fact]
    public async Task ShouldRenewTheToken_AndSendTheRequestAgain_WhenTheServerAnswers401()
    {
        // The server accepts the second token only: the first one was revoked.
        await using var server = new FakeStreamableHttpServer("a") { Authorize = (_, auth) => auth == "Bearer token-2" };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;

        var answer = await transport.SendRequestAsync(Request("tools/list"), Ct);

        Assert.Null(answer.Error);
        Assert.Equal(2, _endpoint.Count);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task ShouldReturnTheError_WhenTheRetryIsRefusedToo()
    {
        await using var server = new FakeStreamableHttpServer("a") { Authorize = (_, _) => false };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendRequestAsync(Request("tools/list"), Ct));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(2, _endpoint.Count);
    }

    [Fact]
    public async Task ShouldRenewOnce_WhenSeveralRequestsAreRefusedAtTheSameTime()
    {
        await using var server = new FakeStreamableHttpServer("a") { Authorize = (_, auth) => auth == "Bearer token-2" };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;

        var answers = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => transport.SendRequestAsync(Request("tools/list"), Ct), Ct)));

        Assert.All(answers, a => Assert.Null(a.Error));
        Assert.Equal(2, _endpoint.Count);
    }

    [Fact]
    public async Task ShouldRenewTheToken_AndOpenTheGetStreamAgain_WhenTheStreamAnswers401()
    {
        var getAuthorizations = new List<string?>();
        await using var server = new FakeStreamableHttpServer("a")
        {
            UseSessions = true,
            AllowGet = true,
            Authorize = (verb, auth) =>
            {
                if (verb != "GET")
                    return true;
                lock (getAuthorizations)
                    getAuthorizations.Add(auth);
                return auth == "Bearer token-2";
            },
        };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;

        var init = await transport.SendRequestAsync(Request("initialize"), Ct);
        Assert.Null(init.Error);
        await transport.SendNotificationAsync(new JsonRpcNotification { Method = "notifications/initialized" }, Ct);
        await Polling.WaitUntilAsync(() => { lock (getAuthorizations) return Task.FromResult(getAuthorizations.Contains("Bearer token-2")); }, Polling.DefaultTimeout);

        server.PushOnGet("notifications/tools/list_changed", "1");
        using var guard = new CancellationTokenSource(Polling.DefaultTimeout);
        var notification = await transport.Notifications.ReadAsync(guard.Token);

        Assert.Equal("notifications/tools/list_changed", notification.Method);
        Assert.Equal(2, _endpoint.Count);
        Assert.Equal(0, _time.PendingTimers);
    }

    [Fact]
    public async Task ShouldRenewOnceThenBackOff_AndRenewAgainAfterTheBackoff_WhenTheGetStreamKeepsAnswering401()
    {
        await using var server = new FakeStreamableHttpServer("a")
        {
            UseSessions = true,
            AllowGet = true,
            // POSTs are let in; the GET stream is always refused.
            Authorize = (verb, _) => verb != "GET",
        };
        var (transport, http) = await OpenAsync(server);
        using var _ = http;
        await using var __ = transport;

        await transport.SendRequestAsync(Request("initialize"), Ct);
        await transport.SendNotificationAsync(new JsonRpcNotification { Method = "notifications/initialized" }, Ct);

        // First 401: renewed at once and tried again; second 401: no renewal, the loop waits out its backoff.
        await Polling.WaitUntilAsync(() => Task.FromResult(server.Requests.Count(r => r.Verb == "GET") == 2 && _time.PendingTimers > 0), Polling.DefaultTimeout);
        Assert.Equal(2, _endpoint.Count);

        // After the backoff the token may have expired meanwhile: it is renewed again, once.
        _time.Advance(TimeSpan.FromMinutes(5));
        await Polling.WaitUntilAsync(() => Task.FromResult(server.Requests.Count(r => r.Verb == "GET") == 4 && _time.PendingTimers > 0), Polling.DefaultTimeout);
        Assert.Equal(3, _endpoint.Count);
    }
}
