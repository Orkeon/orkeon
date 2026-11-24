using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Abstractions.Tests.Auth.Doubles;

namespace Orkeon.Tools.Abstractions.Tests.Auth;

/// <summary>#446: the caller may choose the flow at the sign-in.</summary>
public sealed class OAuthSignInTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly HttpClient _http;
    private readonly OAuth2Client _client;
    private readonly FakeOAuthLoginInteraction _interaction = new();

    public OAuthSignInTests()
    {
        _http = new HttpClient(_handler, disposeHandler: false);
        _client = new OAuth2Client(_http, new ManualTimeProvider(), (_, _) => Task.CompletedTask);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OAuthSettings Settings { get; } = new()
    {
        ClientId = "orkeon-agent",
        Flow = OAuthFlow.DeviceCode,
        TokenEndpoint = new Uri("https://idp.example.test/token"),
        AuthorizationEndpoint = new Uri("https://idp.example.test/authorize"),
        DeviceCodeEndpoint = new Uri("https://idp.example.test/device"),
        Scopes = ["scope.a"],
    };

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private void EnqueueDeviceAnswers()
    {
        _handler.EnqueueJson("""{"device_code":"dc","user_code":"UC","verification_uri":"https://idp.example.test/go"}""");
        _handler.EnqueueJson("""{"access_token":"at-device"}""");
    }

    [Fact]
    public async Task ShouldRunTheLoopback_WhenChosenOverTheDeviceCodeOfTheSettings()
    {
        _handler.EnqueueJson("""{"access_token":"at-loopback"}""");

        var tokens = await OAuthSignIn.RunAsync(_client, Settings, OAuthFlow.LoopbackPkce, null, "work", null, _interaction, Token);

        Assert.Equal("at-loopback", tokens.AccessToken);
        Assert.Equal("idp.example.test", _interaction.Authorization?.Host);
        Assert.Null(_interaction.UserCode);
        Assert.Equal("authorization_code", Assert.Single(_handler.Requests).Form["grant_type"]);
    }

    [Fact]
    public async Task ShouldRunTheDeviceCode_WhenChosenOverTheLoopbackOfTheSettings()
    {
        EnqueueDeviceAnswers();

        var tokens = await OAuthSignIn.RunAsync(
            _client, Settings with { Flow = OAuthFlow.LoopbackPkce }, OAuthFlow.DeviceCode, null, "work", null, _interaction, Token);

        Assert.Equal("at-device", tokens.AccessToken);
        Assert.Equal("UC", _interaction.UserCode);
        Assert.Null(_interaction.Authorization);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldRunTheFlowOfTheSettings_WhenNoneIsChosen(bool device)
    {
        if (device)
            EnqueueDeviceAnswers();
        else
            _handler.EnqueueJson("""{"access_token":"at-loopback"}""");

        var tokens = await OAuthSignIn.RunAsync(
            _client, Settings with { Flow = device ? OAuthFlow.DeviceCode : OAuthFlow.LoopbackPkce }, null, "work", null, _interaction, Token);

        Assert.Equal(device ? "at-device" : "at-loopback", tokens.AccessToken);
    }

    [Theory]
    [InlineData(nameof(OAuthFlow.DeviceCode), "no device authorization endpoint")]
    [InlineData(nameof(OAuthFlow.LoopbackPkce), "no authorization endpoint")]
    [InlineData(nameof(OAuthFlow.ClientCredentials), "no interactive sign-in")]
    public async Task ShouldFailBeforeAnyCall_WhenTheChosenFlowCannotRun(string flow, string reason)
    {
        var bare = Settings with { AuthorizationEndpoint = null, DeviceCodeEndpoint = null };

        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            OAuthSignIn.RunAsync(_client, bare, Enum.Parse<OAuthFlow>(flow), null, "work", null, _interaction, Token));

        Assert.Equal(OAuthFailure.InvalidConfiguration, ex.Failure);
        Assert.Contains("'work'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
        Assert.Null(_interaction.Authorization);
        Assert.Null(_interaction.UserCode);
    }
}
