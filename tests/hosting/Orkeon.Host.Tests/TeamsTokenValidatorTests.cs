using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests;

/// <summary>The check of an incoming Teams activity's bearer token (#446): every way it can be wrong, refused.</summary>
public sealed class TeamsTokenValidatorTests : IDisposable
{
    private readonly FakeBotConnector _microsoft = new();
    private readonly HttpClient _http;
    private readonly BotConnectorKeys _keys;
    private readonly TeamsTokenValidator _validator;

    public TeamsTokenValidatorTests()
    {
        _http = new HttpClient(_microsoft.Transport, disposeHandler: false);
        _keys = new BotConnectorKeys(_http, _microsoft.Time, NullLogger.Instance);
        _validator = new TeamsTokenValidator(_microsoft.Options(), _keys, _microsoft.Time);
    }

    public void Dispose()
    {
        _keys.Dispose();
        _http.Dispose();
        _microsoft.Dispose();
    }

    private Task<TeamsTokenResult> CheckAsync(string token) =>
        _validator.ValidateAsync("Bearer " + token, TestContext.Current.CancellationToken);

    private async Task AssertRefusedAsync(string token) =>
        Assert.Equal(TeamsTokenOutcome.Refused, (await CheckAsync(token)).Outcome);

    [Fact]
    public async Task The_bot_connectors_token_is_accepted_with_its_service_and_endorsements()
    {
        var result = await CheckAsync(_microsoft.Token());

        Assert.Equal((TeamsTokenOutcome.Accepted, string.Empty), (result.Outcome, result.Reason));
        Assert.Equal(new Uri(FakeBotConnector.ServiceUrl), result.ServiceUrl);
        Assert.Equal(["msteams"], result.Endorsements);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Basic YTpi")]
    [InlineData("Bearer not-a-token")]
    [InlineData("Bearer a.b.c")]
    [InlineData("Bearer ..")]
    public async Task A_missing_or_shapeless_token_is_refused_without_a_fetch(string? header)
    {
        var result = await _validator.ValidateAsync(header, TestContext.Current.CancellationToken);

        Assert.Equal(TeamsTokenOutcome.Refused, result.Outcome);
        Assert.Empty(_microsoft.Transport.Requests);
    }

    [Fact]
    public async Task A_forged_signature_is_refused()
    {
        await AssertRefusedAsync(_microsoft.Token(signedWith: _microsoft.AnotherKey()));
    }

    [Fact]
    public async Task A_payload_changed_after_signing_is_refused()
    {
        var parts = _microsoft.Token().Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("/emea/", "/amer/", StringComparison.Ordinal);

        await AssertRefusedAsync($"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}");
    }

    [Fact]
    public async Task An_unsigned_token_is_refused()
    {
        var payload = _microsoft.Token().Split('.')[1];

        await AssertRefusedAsync($"{Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT","kid":"key-1"}""")}.{payload}.");
        await AssertRefusedAsync($"{Base64UrlEncoder.Encode("""{"alg":"RS256","typ":"JWT","kid":"key-1"}""")}.{payload}.");
    }

    [Fact]
    public async Task A_token_signed_with_the_public_key_as_a_secret_is_refused()
    {
        var payload = Base64UrlEncoder.Decode(_microsoft.Token().Split('.')[1]);
        var forged = new JsonWebTokenHandler().CreateToken(payload, new SigningCredentials(
            new SymmetricSecurityKey(_microsoft.PublicKey()) { KeyId = "key-1" }, SecurityAlgorithms.HmacSha256));

        await AssertRefusedAsync(forged);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.RsaSha512)]
    [InlineData(SecurityAlgorithms.RsaSsaPssSha256)]
    public async Task Another_algorithm_is_refused(string algorithm)
    {
        await AssertRefusedAsync(_microsoft.Token(algorithm: algorithm));
    }

    [Fact]
    public async Task A_token_outside_its_lifetime_is_refused_and_the_skew_is_five_minutes()
    {
        await AssertRefusedAsync(_microsoft.Token(notBefore: TimeSpan.FromHours(-2), expires: TimeSpan.FromMinutes(-6)));
        await AssertRefusedAsync(_microsoft.Token(notBefore: TimeSpan.FromMinutes(6)));
        await AssertRefusedAsync(_microsoft.Token(withExpiry: false));

        var late = await CheckAsync(_microsoft.Token(notBefore: TimeSpan.FromHours(-2), expires: TimeSpan.FromMinutes(-4)));
        Assert.Equal(TeamsTokenOutcome.Accepted, late.Outcome);
    }

    [Fact]
    public async Task Another_audience_or_issuer_is_refused_without_a_fetch()
    {
        await AssertRefusedAsync(_microsoft.Token(audience: "another-bot"));
        await AssertRefusedAsync(_microsoft.Token(issuer: "https://sts.windows.net/some-tenant/"));

        Assert.Empty(_microsoft.Transport.Requests);
    }

    [Theory]
    [InlineData("http://smba.trafficmanager.net/emea/")]
    [InlineData("https://evil.example/")]
    [InlineData("https://smba.trafficmanager.net.evil.example/")]
    [InlineData("https://evil@smba.trafficmanager.net/")]
    [InlineData("https://smba.trafficmanager.net:8443/")]
    [InlineData("https://smba.trafficmanager.net/?to=evil")]
    [InlineData("https://smba.trafficmanager.net/#evil")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("/relative")]
    [InlineData("")]
    public async Task A_service_url_replies_must_not_go_to_is_refused_even_when_signed(string serviceUrl)
    {
        await AssertRefusedAsync(_microsoft.Token(serviceUrl: serviceUrl));
    }

    [Fact]
    public async Task No_service_url_or_several_are_refused()
    {
        await AssertRefusedAsync(_microsoft.Token(withServiceUrl: false));
        await AssertRefusedAsync(_microsoft.Token(serviceUrl: new[] { FakeBotConnector.ServiceUrl, "https://smba.trafficmanager.net/amer/" }));
    }

    [Fact]
    public async Task A_service_host_the_operator_added_is_accepted()
    {
        var validator = new TeamsTokenValidator(_microsoft.Options() with { ServiceHosts = ["smba.example.org"] }, _keys, _microsoft.Time);

        var result = await validator.ValidateAsync(
            "Bearer " + _microsoft.Token(serviceUrl: "https://smba.example.org/x/"), TestContext.Current.CancellationToken);

        Assert.Equal(TeamsTokenOutcome.Accepted, result.Outcome);
    }

    // ---- The signing keys ---------------------------------------------------------------

    [Fact]
    public async Task The_keys_are_read_once_then_again_after_their_age()
    {
        await CheckAsync(_microsoft.Token());
        await CheckAsync(_microsoft.Token());
        Assert.Single(_microsoft.MetadataReads);

        _microsoft.Time.Advance(BotConnectorKeys.RefreshAfter);
        await CheckAsync(_microsoft.Token());
        await Polling.WaitUntilAsync(() => _microsoft.MetadataReads.Count == 2);
    }

    [Fact]
    public async Task A_known_key_answers_while_a_slow_refresh_is_under_way()
    {
        await CheckAsync(_microsoft.Token());
        using var gate = new ManualResetEventSlim();
        _microsoft.BeforeMetadata = () => gate.Wait(TestContext.Current.CancellationToken);
        try
        {
            _microsoft.Time.Advance(BotConnectorKeys.RefreshAfter);

            Assert.Equal(TeamsTokenOutcome.Accepted, (await CheckAsync(_microsoft.Token())).Outcome);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task A_slow_metadata_service_is_asked_once_and_the_wait_is_bounded()
    {
        using var gate = new ManualResetEventSlim();
        using var asked = new ManualResetEventSlim();
        _microsoft.BeforeMetadata = () =>
        {
            asked.Set();
            gate.Wait(TestContext.Current.CancellationToken);
        };
        try
        {
            var waiting = Enumerable.Range(0, 5).Select(_ => CheckAsync(_microsoft.Token())).ToArray();
            asked.Wait(TestContext.Current.CancellationToken);
            await Polling.WaitUntilAsync(() =>
            {
                _microsoft.Time.Advance(BotConnectorKeys.RefreshWait);
                return waiting.All(t => t.IsCompleted);
            });

            foreach (var answer in waiting)
                Assert.Equal(TeamsTokenOutcome.KeysUnavailable, (await answer).Outcome);
            Assert.Single(_microsoft.MetadataReads);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task An_unknown_key_refreshes_the_keys_at_most_once_per_interval()
    {
        await CheckAsync(_microsoft.Token());
        for (var i = 0; i < 5; i++)
            await AssertRefusedAsync(_microsoft.Token(keyId: $"unknown-{i}"));
        Assert.Single(_microsoft.MetadataReads);

        _microsoft.Time.Advance(BotConnectorKeys.UnknownKeyInterval);
        await AssertRefusedAsync(_microsoft.Token(keyId: "unknown"));
        await AssertRefusedAsync(_microsoft.Token(keyId: "unknown"));
        Assert.Equal(2, _microsoft.MetadataReads.Count);
    }

    [Fact]
    public async Task A_rotated_key_is_found_after_the_refresh()
    {
        await CheckAsync(_microsoft.Token());
        _microsoft.KeyId = "key-2";
        _microsoft.Time.Advance(BotConnectorKeys.UnknownKeyInterval);

        Assert.Equal(TeamsTokenOutcome.Accepted, (await CheckAsync(_microsoft.Token())).Outcome);
    }

    [Fact]
    public async Task Without_keys_nothing_is_accepted_and_the_service_is_asked_to_come_back()
    {
        _microsoft.Metadata = new FakeHttpResponse(HttpStatusCode.BadGateway, "down");

        Assert.Equal(TeamsTokenOutcome.KeysUnavailable, (await CheckAsync(_microsoft.Token())).Outcome);
        Assert.Equal(TeamsTokenOutcome.KeysUnavailable, (await CheckAsync(_microsoft.Token())).Outcome);
        Assert.Single(_microsoft.MetadataReads);
    }

    [Fact]
    public async Task Keys_that_could_not_be_refreshed_serve_a_day_and_no_longer()
    {
        await CheckAsync(_microsoft.Token());
        _microsoft.Metadata = new FakeHttpResponse(HttpStatusCode.BadGateway, "down");

        _microsoft.Time.Advance(BotConnectorKeys.RefreshAfter);
        Assert.Equal(TeamsTokenOutcome.Accepted, (await CheckAsync(_microsoft.Token())).Outcome);

        _microsoft.Time.Advance(BotConnectorKeys.ExpireAfter - BotConnectorKeys.RefreshAfter);
        Assert.Equal(TeamsTokenOutcome.KeysUnavailable, (await CheckAsync(_microsoft.Token())).Outcome);
    }

    [Theory]
    [InlineData("https://evil.example/keys", BotConnectorKeys.Issuer)]
    [InlineData("http://login.botframework.com/v1/.well-known/keys", BotConnectorKeys.Issuer)]
    [InlineData("https://login.botframework.com:8443/v1/.well-known/keys", BotConnectorKeys.Issuer)]
    [InlineData(FakeBotConnector.KeysAddress, "https://evil.example")]
    public async Task Metadata_naming_keys_elsewhere_is_not_followed(string keysAddress, string issuer)
    {
        _microsoft.Metadata = FakeHttpResponse.Json(FakeBotConnector.MetadataNaming(keysAddress, issuer));

        Assert.Equal(TeamsTokenOutcome.KeysUnavailable, (await CheckAsync(_microsoft.Token())).Outcome);
        Assert.Single(_microsoft.Transport.Requests);
    }
}
