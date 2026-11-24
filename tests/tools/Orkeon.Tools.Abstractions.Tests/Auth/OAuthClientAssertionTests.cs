using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Auth;

namespace Orkeon.Tools.Abstractions.Tests.Auth;

/// <summary>#446: the client credentials grant authenticated by a JWT assertion (RFC 7523).</summary>
public sealed class OAuthClientAssertionTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly HttpClient _http;
    private readonly ManualTimeProvider _time = new();
    private readonly OAuth2Client _client;

    public OAuthClientAssertionTests()
    {
        _http = new HttpClient(_handler, disposeHandler: false);
        _client = new OAuth2Client(_http, _time);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OAuthSettings Settings { get; } = new()
    {
        ClientId = "orkeon-agent",
        Flow = OAuthFlow.ClientCredentials,
        TokenEndpoint = new Uri("https://idp.example.test/token"),
        Scopes = ["scope.a"],
    };

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private static X509Certificate2 NewCertificate(RSA key) =>
        new CertificateRequest("CN=orkeon-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldSignAPs256Assertion_WithTheCertificate(bool keyFirst)
    {
        using var rsa = RSA.Create(2048);
        using var certificate = NewCertificate(rsa);
        var (cert, key) = (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
        _handler.EnqueueJson("""{"access_token":"at-1","expires_in":3599}""");

        var tokens = await _client.RequestClientCredentialsAsync(
            Settings, OAuthClientAssertion.FromCertificatePem(keyFirst ? $"{key}\n{cert}" : $"{cert}\n{key}"), Token);

        Assert.Equal("at-1", tokens.AccessToken);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(Settings.TokenEndpoint, request.Uri);
        Assert.Equal("client_credentials", request.Form["grant_type"]);
        Assert.Equal("orkeon-agent", request.Form["client_id"]);
        Assert.Equal("scope.a", request.Form["scope"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", request.Form["client_assertion_type"]);
        Assert.False(request.Form.ContainsKey("client_secret"));

        var parts = request.Form["client_assertion"].Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
        Assert.Equal("PS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        Assert.Equal(Base64Url.EncodeToString(SHA256.HashData(certificate.RawData)), header.RootElement.GetProperty("x5t#S256").GetString());
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        Assert.Equal("https://idp.example.test/token", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal("orkeon-agent", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal("orkeon-agent", claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal(now, claims.RootElement.GetProperty("nbf").GetInt64());
        Assert.Equal(now, claims.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(now + 300, claims.RootElement.GetProperty("exp").GetInt64());
        Assert.True(Guid.TryParse(claims.RootElement.GetProperty("jti").GetString(), out _));
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    [Fact]
    public async Task ShouldSignANewAssertion_ForEachRequest()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = NewCertificate(rsa);
        var assertion = OAuthClientAssertion.FromCertificatePem(certificate.ExportCertificatePem() + "\n" + rsa.ExportRSAPrivateKeyPem());
        _handler.EnqueueJson("""{"access_token":"at-1"}""");
        _handler.EnqueueJson("""{"access_token":"at-2"}""");

        await _client.RequestClientCredentialsAsync(Settings, assertion, Token);
        await _client.RequestClientCredentialsAsync(Settings, assertion, Token);

        Assert.NotEqual(_handler.Requests[0].Form["client_assertion"], _handler.Requests[1].Form["client_assertion"]);
    }

    [Fact]
    public async Task ShouldSendTheFederatedToken_WithoutTheLineEndOfItsFile()
    {
        _handler.EnqueueJson("""{"access_token":"at-1"}""");

        await _client.RequestClientCredentialsAsync(Settings, OAuthClientAssertion.FromFederatedToken("aaa.bbb.ccc\n"), Token);

        var form = Assert.Single(_handler.Requests).Form;
        Assert.Equal("aaa.bbb.ccc", form["client_assertion"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", form["client_assertion_type"]);
    }

    [Fact]
    public async Task ShouldScrubTheAssertion_FromARefusedGrant()
    {
        _handler.Enqueue(new FakeHttpResponse(HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"cannot accept aaa.bbb.ccc"}"""));

        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            _client.RequestClientCredentialsAsync(Settings, OAuthClientAssertion.FromFederatedToken("aaa.bbb.ccc"), Token));

        Assert.Equal(OAuthFailure.AuthenticationFailed, ex.Failure);
        Assert.Contains("invalid_client", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("aaa.bbb.ccc", ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    public void ShouldRefuseAnEmptyCredential(string text)
    {
        Assert.Equal(OAuthFailure.InvalidConfiguration, Assert.Throws<OAuthException>(() => OAuthClientAssertion.FromFederatedToken(text)).Failure);
        Assert.Equal(OAuthFailure.InvalidConfiguration, Assert.Throws<OAuthException>(() => OAuthClientAssertion.FromCertificatePem(text)).Failure);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("missing")]
    [InlineData("other")]
    [InlineData("encrypted")]
    [InlineData("garbage")]
    public void ShouldRefuseACertificate_WithoutItsOwnReadableKey(string kind)
    {
        using var rsa = RSA.Create(2048);
        using var other = RSA.Create(2048);
        using var certificate = NewCertificate(rsa);
        var cert = certificate.ExportCertificatePem() + "\n";
        var pem = kind switch
        {
            "none" => "canary-not-a-pem",
            "missing" => cert,
            "other" => cert + other.ExportPkcs8PrivateKeyPem(),
            "encrypted" => cert + rsa.ExportEncryptedPkcs8PrivateKeyPem("pw", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000)),
            _ => cert + PemEncoding.WriteString("PRIVATE KEY", "canary"u8),
        };

        var ex = Assert.Throws<OAuthException>(() => OAuthClientAssertion.FromCertificatePem(pem));

        Assert.Equal(OAuthFailure.InvalidConfiguration, ex.Failure);
        Assert.DoesNotContain("canary", ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Y2FuYXJ5", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseAKey_ThatIsNotRsa()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = new CertificateRequest("CN=orkeon-test", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(100));

        var ex = Assert.Throws<OAuthException>(() =>
            OAuthClientAssertion.FromCertificatePem(certificate.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem()));

        Assert.Equal(OAuthFailure.InvalidConfiguration, ex.Failure);
        Assert.Contains("RSA", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNotPrintTheCredential_WhenFormatted() =>
        Assert.DoesNotContain("aaa.bbb.ccc", Convert.ToString((object)OAuthClientAssertion.FromFederatedToken("aaa.bbb.ccc"), System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
