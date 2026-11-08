using System.Net;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Tools.Abstractions.Tests.Auth;

/// <summary>FWK-16: the client credentials grant (RFC 6749 section 4.4) of the hand-written OAuth client.</summary>
public sealed class OAuth2ClientCredentialsTests : IDisposable
{
    // A canary: it must never come back out of the client in a message.
    private const string Canary = "canary-7f3a-do-not-leak";

    private readonly FakeHttpMessageHandler _handler = new();
    private readonly HttpClient _http;
    private readonly ManualTimeProvider _time = new();
    private readonly OAuth2Client _client;

    public OAuth2ClientCredentialsTests()
    {
        _http = new HttpClient(_handler, disposeHandler: false);
        _client = new OAuth2Client(_http, _time);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OAuthSettings Settings { get; } = new()
    {
        ClientId = "orkeon-agent",
        Flow = OAuthFlow.ClientCredentials,
        TokenEndpoint = new Uri("https://idp.example.test/oauth/token"),
        Scopes = ["tools.read", "tools.call"],
        Audience = "https://mcp.example.test",
    };

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task ShouldPostTheGrant_WithTheClientScopesAndAudience()
    {
        _handler.EnqueueJson("""{"access_token":"at-1","token_type":"Bearer","expires_in":1800}""");

        var tokens = await _client.RequestClientCredentialsAsync(Settings, Canary, Token);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Settings.TokenEndpoint, request.Uri);
        Assert.Equal("client_credentials", request.Form["grant_type"]);
        Assert.Equal("orkeon-agent", request.Form["client_id"]);
        Assert.Equal(Canary, request.Form["client_secret"]);
        Assert.Equal("tools.read tools.call", request.Form["scope"]);
        Assert.Equal("https://mcp.example.test", request.Form["audience"]);
        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Assert.Equal(_time.GetUtcNow().AddSeconds(1800), tokens.ExpiresAt);
    }

    [Fact]
    public async Task ShouldOmitScopeAndAudience_WhenTheSettingsHaveNone()
    {
        _handler.EnqueueJson("""{"access_token":"at-1","expires_in":60}""");
        var bare = Settings with { Scopes = [], Audience = null };

        await _client.RequestClientCredentialsAsync(bare, Canary, Token);

        var form = Assert.Single(_handler.Requests).Form;
        Assert.False(form.ContainsKey("scope"));
        Assert.False(form.ContainsKey("audience"));
    }

    [Fact]
    public async Task ShouldRefuseTheGrant_WithTheProvidersErrorAndNoCredential()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.Unauthorized,
            $$"""{"error":"invalid_client","error_description":"bad credentials {{Canary}} for orkeon-agent"}"""));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _client.RequestClientCredentialsAsync(Settings, Canary, Token));

        Assert.Equal(OAuthFailure.AuthenticationFailed, ex.Failure);
        Assert.Contains("invalid_client", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFail_WhenTheAnswerHasNoAccessToken()
    {
        _handler.EnqueueJson("""{"token_type":"Bearer"}""");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _client.RequestClientCredentialsAsync(Settings, Canary, Token));

        Assert.Equal(OAuthFailure.ServerError, ex.Failure);
    }

    [Fact]
    public async Task ShouldRefuseAnEmptyClientCredential()
    {
        await Assert.ThrowsAsync<OAuthException>(() => _client.RequestClientCredentialsAsync(Settings, "", Token));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public void ShouldNotPrintTheTokens_WhenATokenSetIsFormatted()
    {
        var tokens = new OAuthTokenSet
        {
            AccessToken = "access-" + Canary,
            RefreshToken = "refresh-" + Canary,
            ExpiresAt = _time.GetUtcNow(),
        };

        var text = tokens.ToString();

        Assert.DoesNotContain(Canary, text, StringComparison.Ordinal);
        Assert.Contains("ExpiresAt", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldScrubTheCredentials_FromARefusedRefresh()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.BadRequest,
            $$"""{"error":"invalid_request","error_description":"bad {{Canary}} and refresh-{{Canary}}"}"""));

        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            _client.RefreshAsync(Settings with { Flow = OAuthFlow.DeviceCode }, Canary, "refresh-" + Canary, Token));

        Assert.DoesNotContain(Canary, ex.Message, StringComparison.Ordinal);
        Assert.Contains("invalid_request", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldScrubTheCredentials_FromAFailedDevicePoll()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.BadRequest,
            $$"""{"error":"invalid_grant","error_description":"device {{Canary}} unknown, secret {{Canary}}x"}"""));
        var grant = new DeviceCodeGrant("device-" + Canary, "UC", new Uri("https://idp.example.test/activate"), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
        var client = new OAuth2Client(_http, _time, (_, _) => Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            client.PollDeviceCodeAsync(Settings with { Flow = OAuthFlow.DeviceCode }, Canary, grant, Token));

        Assert.DoesNotContain(Canary, ex.Message, StringComparison.Ordinal);
        Assert.Contains("invalid_grant", ex.Message, StringComparison.Ordinal);
    }
}
