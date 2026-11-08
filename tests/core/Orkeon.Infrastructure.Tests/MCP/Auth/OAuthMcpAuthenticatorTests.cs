using System.Net;
using Orkeon.Infrastructure.MCP.Auth;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Infrastructure.Tests.MCP.Auth;

/// <summary>FWK-16: the OAuth authenticator of an MCP server: in-memory cache, refresh 60 s ahead, one renewal per 401.</summary>
public sealed class OAuthMcpAuthenticatorTests : IDisposable
{
    // A canary: it must never come back out of the authenticator in a message.
    private const string Canary = "canary-7f3a-do-not-leak";
    private const string CredentialVariable = "TRACKER_OAUTH_CLIENT_KEY";

    private readonly StubTokenEndpointHandler _endpoint = new();
    private readonly HttpClient _http;
    private readonly ManualTimeProvider _time = new();
    private readonly Dictionary<string, string?> _environment = new() { [CredentialVariable] = Canary };

    public OAuthMcpAuthenticatorTests() => _http = new HttpClient(_endpoint, disposeHandler: false);

    public void Dispose()
    {
        _http.Dispose();
        _endpoint.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OAuthSettings ClientCredentials { get; } = new()
    {
        ClientId = "orkeon-agent",
        Flow = OAuthFlow.ClientCredentials,
        TokenEndpoint = new Uri("https://idp.example.test/oauth/token"),
        Scopes = ["tools.call"],
    };

    private static OAuthSettings Device { get; } = new()
    {
        ClientId = "orkeon-cli",
        Flow = OAuthFlow.DeviceCode,
        TokenEndpoint = new Uri("https://idp.example.test/oauth/token"),
        DeviceCodeEndpoint = new Uri("https://idp.example.test/oauth/device"),
        Scopes = ["tools.call"],
    };

    private OAuthMcpAuthenticator Create(OAuthSettings settings, IOAuthLoginInteraction? interaction = null) =>
        new("tracker", new OAuth2Client(_http, _time, (wait, _) =>
        {
            _time.Advance(wait);
            return Task.CompletedTask;
        }), settings, CredentialVariable, name => _environment.GetValueOrDefault(name), interaction, _time);

    private static async Task<string?> AuthorizationAsync(OAuthMcpAuthenticator authenticator)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.example.test/rpc");
        await authenticator.ApplyAsync(request, Ct);
        return request.Headers.Authorization?.ToString();
    }

    [Fact]
    public async Task ShouldGetOneTokenAndKeepItInMemory_ForTheNextRequests()
    {
        using var authenticator = Create(ClientCredentials);

        var first = await AuthorizationAsync(authenticator);
        var second = await AuthorizationAsync(authenticator);

        Assert.Equal("Bearer token-1", first);
        Assert.Equal(first, second);
        var form = Assert.Single(_endpoint.Forms);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal(Canary, form["client_secret"]);
    }

    [Fact]
    public async Task ShouldRenewTheToken_SixtySecondsBeforeItExpires()
    {
        _endpoint.ExpiresIn = 3600;
        using var authenticator = Create(ClientCredentials);
        Assert.Equal("Bearer token-1", await AuthorizationAsync(authenticator));

        _time.Advance(TimeSpan.FromSeconds(3600 - 61));
        Assert.Equal("Bearer token-1", await AuthorizationAsync(authenticator));
        Assert.Equal(1, _endpoint.Count);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("Bearer token-2", await AuthorizationAsync(authenticator));
        Assert.Equal(2, _endpoint.Count);
    }

    [Fact]
    public async Task ShouldRenewTheToken_WhenTheServerRefusedIt_AndSayWhetherToRetry()
    {
        using var authenticator = Create(ClientCredentials);
        var rejected = await AuthorizationAsync(authenticator);

        var retry = await authenticator.HandleUnauthorizedAsync(rejected, Ct);

        Assert.True(retry);
        Assert.Equal("Bearer token-2", await AuthorizationAsync(authenticator));
        Assert.Equal(2, _endpoint.Count);
    }

    [Fact]
    public async Task ShouldRenewOnlyOnce_WhenSeveralRequestsAreRefusedTogether()
    {
        using var authenticator = Create(ClientCredentials);
        var rejected = await AuthorizationAsync(authenticator);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _endpoint.Gate = gate;

        var callers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () => await authenticator.HandleUnauthorizedAsync(rejected, Ct), Ct))
            .ToList();
        await Polling.WaitUntilAsync(() => Task.FromResult(_endpoint.Count >= 2), Polling.DefaultTimeout);
        // All eight are inside, the first renewing and the seven others waiting on it.
        await Polling.WaitUntilAsync(() => Task.FromResult(authenticator.WaitingCount == 8), Polling.DefaultTimeout);
        gate.SetResult();
        var answers = await Task.WhenAll(callers);

        Assert.All(answers, Assert.True);
        Assert.Equal(2, _endpoint.Count);
        Assert.Equal("Bearer token-2", await AuthorizationAsync(authenticator));
    }

    [Fact]
    public async Task ShouldNotRenewAgain_WhenAnotherRequestAlreadyReplacedTheRejectedToken()
    {
        using var authenticator = Create(ClientCredentials);
        var stale = await AuthorizationAsync(authenticator);
        await authenticator.HandleUnauthorizedAsync(stale, Ct);

        var retry = await authenticator.HandleUnauthorizedAsync(stale, Ct);

        Assert.True(retry);
        Assert.Equal(2, _endpoint.Count);
    }

    [Fact]
    public async Task ShouldKeepTheCredentialOutOfTheErrors_WhenTheProviderEchoesIt()
    {
        _endpoint.Respond = _ => (HttpStatusCode.Unauthorized,
            $$"""{"error":"invalid_client","error_description":"value {{Canary}} is wrong"}""");
        using var authenticator = Create(ClientCredentials);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => AuthorizationAsync(authenticator));

        Assert.DoesNotContain(Canary, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, ex.ToString(), StringComparison.Ordinal);
        Assert.Contains("tracker", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailNamingTheVariable_WhenItIsNotSetAtRequestTime()
    {
        using var authenticator = Create(ClientCredentials);
        _environment[CredentialVariable] = null;

        var ex = await Assert.ThrowsAsync<McpServerConfigurationException>(() => AuthorizationAsync(authenticator));

        Assert.Contains(CredentialVariable, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, _endpoint.Count);
    }

    [Fact]
    public async Task ShouldRunTheDeviceFlowOnce_ThenRefreshWithTheRefreshToken()
    {
        _endpoint.Respond = form => form.GetValueOrDefault("grant_type") switch
        {
            null => (HttpStatusCode.OK,
                """{"device_code":"dev-1","user_code":"WXYZ-1234","verification_uri":"https://idp.example.test/activate","expires_in":600,"interval":5}"""),
            "urn:ietf:params:oauth:grant-type:device_code" => (HttpStatusCode.OK,
                """{"access_token":"dev-token","refresh_token":"dev-refresh","expires_in":3600}"""),
            "refresh_token" => (HttpStatusCode.OK, """{"access_token":"renewed-token","expires_in":3600}"""),
            _ => (HttpStatusCode.BadRequest, """{"error":"unsupported_grant_type"}"""),
        };
        var interaction = new FakeOAuthLoginInteraction();
        using var authenticator = Create(Device, interaction);

        Assert.Equal("Bearer dev-token", await AuthorizationAsync(authenticator));
        Assert.Equal("Bearer dev-token", await AuthorizationAsync(authenticator));
        var shown = Assert.Single(interaction.DeviceCodes);
        Assert.Equal("tracker", shown.Account);
        Assert.Equal("WXYZ-1234", shown.UserCode);
        Assert.Equal(new Uri("https://idp.example.test/activate"), shown.VerificationUri);

        _time.Advance(TimeSpan.FromSeconds(3600));
        Assert.Equal("Bearer renewed-token", await AuthorizationAsync(authenticator));
        Assert.Single(interaction.DeviceCodes);
        Assert.Equal("dev-refresh", _endpoint.Forms[^1]["refresh_token"]);
    }

    [Fact]
    public async Task ShouldKeepTheTokensOutOfTheErrors_WhenTheRefreshIsRefusedWithAnEcho()
    {
        _endpoint.Respond = form => form.GetValueOrDefault("grant_type") switch
        {
            null => (HttpStatusCode.OK,
                """{"device_code":"dev-1","user_code":"WXYZ-1234","verification_uri":"https://idp.example.test/activate","expires_in":600,"interval":5}"""),
            "urn:ietf:params:oauth:grant-type:device_code" => (HttpStatusCode.OK,
                """{"access_token":"dev-token","refresh_token":"dev-refresh","expires_in":3600}"""),
            _ => (HttpStatusCode.ServiceUnavailable,
                """{"error":"temporarily_unavailable","error_description":"cannot renew dev-refresh now, access dev-token kept"}"""),
        };
        using var authenticator = Create(Device, new FakeOAuthLoginInteraction());
        await AuthorizationAsync(authenticator);
        _time.Advance(TimeSpan.FromSeconds(3600));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => AuthorizationAsync(authenticator));

        Assert.Contains("temporarily_unavailable", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("dev-refresh", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("dev-token", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldGiveEveryWaiterTheSameError_WithOneTokenRequest_WhenTheRenewalFails()
    {
        var calls = 0;
        _endpoint.Respond = _ => Interlocked.Increment(ref calls) == 1
            ? (HttpStatusCode.OK, """{"access_token":"token-1","expires_in":3600}""")
            : (HttpStatusCode.ServiceUnavailable, """{"error":"temporarily_unavailable","error_description":"try later"}""");
        using var authenticator = Create(ClientCredentials);
        var rejected = await AuthorizationAsync(authenticator);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _endpoint.Gate = gate;

        var callers = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(async () => await Record.ExceptionAsync(async () => await authenticator.HandleUnauthorizedAsync(rejected, Ct)), Ct))
            .ToList();
        await Polling.WaitUntilAsync(() => Task.FromResult(authenticator.WaitingCount == 6), Polling.DefaultTimeout);
        gate.SetResult();
        var errors = await Task.WhenAll(callers);

        Assert.All(errors, e =>
        {
            var failure = Assert.IsType<OAuthException>(e);
            Assert.Contains("temporarily_unavailable", failure.Message, StringComparison.Ordinal);
        });
        Assert.Equal(2, _endpoint.Count);
    }

    [Fact]
    public async Task ShouldTryAgain_WhenALaterRequestIsRefusedAfterAFailedRenewal()
    {
        var calls = 0;
        _endpoint.Respond = _ => Interlocked.Increment(ref calls) switch
        {
            1 => (HttpStatusCode.OK, """{"access_token":"token-1","expires_in":3600}"""),
            2 => (HttpStatusCode.ServiceUnavailable, """{"error":"temporarily_unavailable"}"""),
            _ => (HttpStatusCode.OK, """{"access_token":"token-3","expires_in":3600}"""),
        };
        using var authenticator = Create(ClientCredentials);
        var rejected = await AuthorizationAsync(authenticator);
        await Assert.ThrowsAsync<OAuthException>(async () => await authenticator.HandleUnauthorizedAsync(rejected, Ct));

        var retry = await authenticator.HandleUnauthorizedAsync(rejected, Ct);

        Assert.True(retry);
        Assert.Equal("Bearer token-3", await AuthorizationAsync(authenticator));
    }

    [Fact]
    public async Task ShouldLeaveAShortValueAlone_WhenScrubbingAnError()
    {
        // Three characters: too short to blank without wrecking the sentence.
        _environment[CredentialVariable] = "abc";
        _endpoint.Respond = _ => (HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"abc is wrong"}""");
        using var authenticator = Create(ClientCredentials);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => AuthorizationAsync(authenticator));

        Assert.Contains("abc is wrong", ex.Message, StringComparison.Ordinal);
    }
}
