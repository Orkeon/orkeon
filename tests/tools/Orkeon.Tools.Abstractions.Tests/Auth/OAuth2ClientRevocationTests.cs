using System.Net;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Tools.Abstractions.Tests.Auth;

/// <summary>#446: the refresh token is revoked (RFC 7009) where the provider has an endpoint for it.</summary>
public sealed class OAuth2ClientRevocationTests : IDisposable
{
    private const string Canary = "canary-7f3a-do-not-leak";

    private readonly FakeHttpMessageHandler _handler = new();
    private readonly HttpClient _http;
    private readonly OAuth2Client _client;

    public OAuth2ClientRevocationTests()
    {
        _http = new HttpClient(_handler, disposeHandler: false);
        _client = new OAuth2Client(_http, new ManualTimeProvider());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OAuthSettings Settings { get; } = new()
    {
        ClientId = "orkeon-agent",
        Flow = OAuthFlow.LoopbackPkce,
        TokenEndpoint = new Uri("https://idp.example.test/token"),
        RevocationEndpoint = new Uri("https://idp.example.test/revoke"),
        Scopes = ["scope.a"],
    };

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("revoked, not JSON")]
    public async Task ShouldPostTheRefreshToken_WhateverTheBodyOfTheSuccess(string body)
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.OK, body));

        Assert.True(await _client.RevokeAsync(Settings, "secret-1", "rt-1", Token));

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Settings.RevocationEndpoint, request.Uri);
        Assert.Equal("rt-1", request.Form["token"]);
        Assert.Equal("refresh_token", request.Form["token_type_hint"]);
        Assert.Equal("orkeon-agent", request.Form["client_id"]);
        Assert.Equal("secret-1", request.Form["client_secret"]);
    }

    [Fact]
    public async Task ShouldSendNoSecret_WhenTheClientHasNone()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.OK, ""));

        Assert.True(await _client.RevokeAsync(Settings, null, "rt-1", Token));

        Assert.False(Assert.Single(_handler.Requests).Form.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task ShouldSendNothing_WhenTheProviderHasNoRevocationEndpoint()
    {
        Assert.False(await _client.RevokeAsync(Settings with { RevocationEndpoint = null }, null, "rt-1", Token));
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task ShouldThrowWithoutTheCredentials_WhenTheRevocationIsRefused(HttpStatusCode status, bool refused)
    {
        _handler.Enqueue(new FakeHttpResponse(status,
            $$"""{"error":"unsupported_token_type","error_description":"rt-{{Canary}} with secret-{{Canary}}"}"""));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _client.RevokeAsync(Settings, "secret-" + Canary, "rt-" + Canary, Token));

        Assert.Equal(refused ? OAuthFailure.AuthenticationFailed : OAuthFailure.ServerError, ex.Failure);
        Assert.Contains("unsupported_token_type", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameTheStatus_WhenTheRefusalHasNoError()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.Forbidden, ""));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => _client.RevokeAsync(Settings, null, "rt-1", Token));

        Assert.Contains("HTTP 403", ex.Message, StringComparison.Ordinal);
    }
}
