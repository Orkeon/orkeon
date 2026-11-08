using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Host.Tests.Api;

/// <summary>FWK-14: the schemes the API shares with A2A, and the refusal to listen beyond the loopback unauthenticated.</summary>
public sealed class HostApiAuthTests : IDisposable
{
    private static readonly string[] s_apiKey = ["ApiKey"];
    private static readonly string[] s_apiKeyName = ["API_KEY"];
    private static readonly string[] s_bearer = ["Bearer"];

    private readonly HostApiHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void ApiKey(Dictionary<string, object?> root, Dictionary<string, object?> api)
    {
        api["Security"] = new { AllowedAuthSchemes = s_apiKey, ApiKeySecretNames = s_apiKeyName };
        root["Secrets"] = new { API_KEY = "secret" };
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task An_api_key_is_required_on_every_run_route_and_not_on_the_probes()
    {
        var llm = new HeldLlmProvider("the answer");
        await using var daemon = await _harness.StartAsync(llm, configure: ApiKey);

        using var anonymous = await daemon.StartRunAsync();
        using var wrong = await daemon.StartRunAsync(authorization: "ApiKey nope");
        using var list = await daemon.GetAsync("/runs");
        using var probe = await daemon.GetAsync("/readyz");
        using var accepted = await daemon.StartRunAsync(authorization: "ApiKey secret");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_bearer_token_a_registered_validator_accepts_passes_and_any_other_is_a_401()
    {
        var llm = new HeldLlmProvider("the answer");
        await using var daemon = await _harness.StartAsync(llm,
            configure: (_, api) => api["Security"] = new { AllowedAuthSchemes = s_bearer },
            services: s => s.AddSingleton<IAuthenticationProvider>(new StubAuthenticationProvider("good-token")));

        using var wrong = await daemon.GetAsync("/runs", "Bearer other");
        using var good = await daemon.GetAsync("/runs", "Bearer good-token");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_bearer_scheme_without_a_validator_refuses_the_start()
    {
        using var host = _harness.Build(
            new HeldLlmProvider("unused"), LoopbackPorts.Probe(), configure: (_, api) => api["Security"] = new { AllowedAuthSchemes = s_bearer });

        var error = await Assert.ThrowsAsync<HostConfigurationException>(() => host.StartAsync(Ct));

        Assert.Contains("Bearer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Listening_beyond_the_loopback_without_authentication_refuses_the_start()
    {
        using var host = _harness.Build(
            new HeldLlmProvider("unused"), LoopbackPorts.Probe(), configure: (_, api) => api["Prefix"] = $"http://+:{LoopbackPorts.Probe()}/");

        var error = await Assert.ThrowsAsync<HostConfigurationException>(() => host.StartAsync(Ct));

        Assert.Contains("Orkeon:Host:Api:Security", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_prefix_without_its_closing_slash_refuses_the_start()
    {
        using var host = _harness.Build(
            new HeldLlmProvider("unused"), LoopbackPorts.Probe(), configure: (_, api) => api["Prefix"] = "http://localhost:5190");

        var error = await Assert.ThrowsAsync<HostConfigurationException>(() => host.StartAsync(Ct));

        Assert.Contains("Orkeon:Host:Api:Prefix", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Without_the_section_the_host_listens_on_no_api_port()
    {
        using var refusing = LoopbackPorts.Refusing();
        using var host = _harness.Build(
            new HeldLlmProvider("unused"), refusing.Port, configure: (_, api) => api["Enabled"] = false);

        await host.StartAsync(Ct);
        try
        {
            using var http = new HttpClient();
            await Assert.ThrowsAsync<HttpRequestException>(
                () => http.GetAsync(new Uri($"{LoopbackPorts.Host}:{refusing.Port}/healthz"), Ct));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
