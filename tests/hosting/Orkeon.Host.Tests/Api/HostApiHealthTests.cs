using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests.Api;

/// <summary>FWK-14: the probes. Liveness is local; readiness is what the documentation says it is.</summary>
public sealed class HostApiHealthTests : IDisposable
{
    private static readonly string[] s_apiKey = ["ApiKey"];
    private static readonly string[] s_apiKeyName = ["API_KEY"];
    private static readonly string[] s_llmTag = ["llm"];

    private readonly HostApiHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string[] Names(JsonObject probe) =>
        [.. probe["checks"]!.AsArray().Select(c => c!["name"]!.GetValue<string>())];

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Liveness_answers_200_without_authentication_and_never_touches_the_llm()
    {
        var llm = new HeldLlmProvider("unused", held: true);
        await using var daemon = await _harness.StartAsync(llm, configure: (root, api) =>
        {
            api["Security"] = new { AllowedAuthSchemes = s_apiKey, ApiKeySecretNames = s_apiKeyName };
            root["Secrets"] = new { API_KEY = "secret" };
        });

        using var response = await daemon.GetAsync("/healthz");
        var probe = await HostApiHarness.Daemon.JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["system_resources"], Names(probe));
        Assert.Empty(llm.Prompts);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Readiness_checks_the_registry_the_state_store_the_history_and_the_memory_provider_but_not_the_llm()
    {
        var llm = new HeldLlmProvider("unused", held: true);
        await using var daemon = await _harness.StartAsync(llm);

        using var response = await daemon.GetAsync("/readyz");
        var probe = await HostApiHarness.Daemon.JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["crew_registry", "memory_provider", "run_history", "state_store"], Names(probe));
        Assert.Empty(llm.Prompts);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Readiness_includes_the_llm_check_when_the_operator_lists_its_tag()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"), configure: (_, api) => api["ReadinessTags"] = s_llmTag);

        using var response = await daemon.GetAsync("/readyz");

        Assert.Contains("llm_provider", Names(await HostApiHarness.Daemon.JsonAsync(response)));
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Readiness_is_503_when_the_state_store_throws_and_200_again_when_it_recovers()
    {
        var store = new FakeStateStore();
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (_, api) => api["ReadinessCacheTtl"] = "00:00:00",
            services: s => s.AddSingleton<IStateStore>(store));

        store.Throws = true;
        using var down = await daemon.GetAsync("/readyz");
        var probe = await HostApiHarness.Daemon.JsonAsync(down);
        store.Throws = false;
        using var up = await daemon.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Equal("unhealthy", probe["status"]!.GetValue<string>());
        var failed = probe["checks"]!.AsArray().Single(c => c!["status"]!.GetValue<string>() == "unhealthy");
        Assert.Equal("state_store", failed!["name"]!.GetValue<string>());
        Assert.DoesNotContain("the store is down", probe.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Both_probes_answer_503_once_the_stop_has_closed_admission()
    {
        await using var daemon = await _harness.StartAsync(new HeldLlmProvider("unused"));

        daemon.Registry.CloseAdmission();
        using var live = await daemon.GetAsync("/healthz");
        using var ready = await daemon.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, live.StatusCode);
        Assert.Equal("stopping", (await HostApiHarness.Daemon.JsonAsync(live))["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }
}
