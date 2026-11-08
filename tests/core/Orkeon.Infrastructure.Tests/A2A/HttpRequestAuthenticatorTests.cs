using System.Net;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// FWK-14: the authentication extracted from <c>A2AServer</c> behaves as it did there (same verdicts, same statuses),
/// over a real <see cref="HttpListener"/> on the loopback.
/// </summary>
public sealed class HttpRequestAuthenticatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Asks the authenticator about one request and returns its verdict.</summary>
    private static async Task<HttpAuthorization> AskAsync(HttpRequestAuthenticator authenticator, string? authorization)
    {
        var (listener, prefix) = LoopbackPorts.StartListener();
        try
        {
#pragma warning disable CA2000, CA2025 // The client, the request and the response outlive the awaited send below.
            var http = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, prefix);
            if (authorization is not null)
                request.Headers.TryAddWithoutValidation("Authorization", authorization);

            var sending = http.SendAsync(request, Ct);
            var context = await listener.GetContextAsync().WaitAsync(Ct);
            var verdict = await authenticator.AuthorizeAsync(context, Ct);
            context.Response.StatusCode = verdict.StatusCode;
            context.Response.Close();
            using var response = await sending;
            Assert.Equal(verdict.StatusCode, (int)response.StatusCode);
            request.Dispose();
            http.Dispose();
            return verdict;
#pragma warning restore CA2000, CA2025
        }
        finally
        {
            listener.Abort();
        }
    }

    [Fact]
    public async Task Without_a_declared_rule_every_request_passes()
    {
        using var authenticator = new HttpRequestAuthenticator(new HttpSecurityOptions());

        Assert.False(authenticator.IsRequired);
        Assert.True((await AskAsync(authenticator, null)).Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer wrong-token")]
    public async Task A_missing_malformed_or_refused_bearer_credential_is_a_401(string? header)
    {
        using var authenticator = new HttpRequestAuthenticator(
            new HttpSecurityOptions { AllowedAuthSchemes = { "Bearer" } }, [new StubAuthenticationProvider("good-token")]);

        var verdict = await AskAsync(authenticator, header);

        Assert.False(verdict.Allowed);
        Assert.Equal(401, verdict.StatusCode);
        Assert.DoesNotContain("good-token", verdict.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bearer_token_a_provider_validates_passes()
    {
        using var authenticator = new HttpRequestAuthenticator(
            new HttpSecurityOptions { AllowedAuthSchemes = { "Bearer" } }, [new StubAuthenticationProvider("good-token")]);

        Assert.True((await AskAsync(authenticator, "Bearer good-token")).Allowed);
    }

    [Fact]
    public async Task An_api_key_is_read_from_the_secret_provider_on_every_request()
    {
        var secrets = new MockSecretProvider();
        secrets.AddSecret("API_KEY", "k-123456");
        using var authenticator = new HttpRequestAuthenticator(
            new HttpSecurityOptions { AllowedAuthSchemes = { "ApiKey" }, ApiKeySecretNames = { "API_KEY" } }, secretProvider: secrets);

        Assert.True((await AskAsync(authenticator, "ApiKey k-123456")).Allowed);
        Assert.Equal(401, (await AskAsync(authenticator, "ApiKey k-654321")).StatusCode);
    }

    [Fact]
    public async Task Mutual_tls_without_a_client_certificate_is_a_403()
    {
        using var authenticator = new HttpRequestAuthenticator(
            new HttpSecurityOptions { RequireMutualTls = true, TrustedClientCertificateThumbprints = { "AB" } });

        var verdict = await AskAsync(authenticator, null);

        Assert.Equal(403, verdict.StatusCode);
    }

    [Fact]
    public async Task A_declared_scheme_without_its_validator_refuses_the_start()
    {
        using var bearerless = new HttpRequestAuthenticator(new HttpSecurityOptions { AllowedAuthSchemes = { "Bearer" } });
        using var anchorless = new HttpRequestAuthenticator(new HttpSecurityOptions { RequireMutualTls = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => bearerless.EnsureReadyAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => anchorless.EnsureReadyAsync(Ct));
    }

    [Fact]
    public async Task The_start_refusals_name_the_section_of_the_caller_not_the_a2a_one()
    {
        const string section = "Orkeon:Host:Api:Security";
        using var bearerless = new HttpRequestAuthenticator(
            new HttpSecurityOptions { AllowedAuthSchemes = { "Bearer" } }, sectionName: section);
        using var keyless = new HttpRequestAuthenticator(
            new HttpSecurityOptions { AllowedAuthSchemes = { "ApiKey" } }, sectionName: section);
        using var odd = new HttpRequestAuthenticator(
            new HttpSecurityOptions { AllowedAuthSchemes = { "Basic" } }, sectionName: section);
        using var a2a = new HttpRequestAuthenticator(new HttpSecurityOptions { AllowedAuthSchemes = { "ApiKey" } });

        var messages = new[]
        {
            (await Assert.ThrowsAsync<InvalidOperationException>(() => bearerless.EnsureReadyAsync(Ct))).Message,
            (await Assert.ThrowsAsync<InvalidOperationException>(() => keyless.EnsureReadyAsync(Ct))).Message,
            (await Assert.ThrowsAsync<InvalidOperationException>(() => odd.EnsureReadyAsync(Ct))).Message,
        };
        var a2aMessage = (await Assert.ThrowsAsync<InvalidOperationException>(() => a2a.EnsureReadyAsync(Ct))).Message;

        Assert.All(messages, m => Assert.Contains(section + ":AllowedAuthSchemes", m, StringComparison.Ordinal));
        Assert.All(messages, m => Assert.DoesNotContain("A2A:Security:AllowedAuthSchemes", m, StringComparison.Ordinal));
        Assert.Contains(section + ":ApiKeySecretNames", messages[1], StringComparison.Ordinal);
        Assert.Contains("A2A:Security:AllowedAuthSchemes", a2aMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_http_options_copy_the_server_side_rules_of_the_a2a_options()
    {
        var a2a = new A2ASecurityOptions
        {
            RequireMutualTls = true,
            AllowedAuthSchemes = { "ApiKey" },
            ApiKeySecretNames = { "K" },
            TrustedCertificateAuthorities = { "/ca.pem" },
            TrustedClientCertificateThumbprints = { "AB" },
        };

        var http = HttpSecurityOptions.From(a2a);

        Assert.True(http.RequireMutualTls);
        Assert.Equal(["ApiKey"], http.AllowedAuthSchemes);
        Assert.Equal(["K"], http.ApiKeySecretNames);
        Assert.Equal(["/ca.pem"], http.TrustedCertificateAuthorities);
        Assert.Equal(["AB"], http.TrustedClientCertificateThumbprints);
    }
}
