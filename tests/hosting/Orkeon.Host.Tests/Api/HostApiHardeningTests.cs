using System.Net;
using Orkeon.Host.Api;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests.Api;

/// <summary>FWK final review H2, H4, H8, H16, H18: the API's guards when it listens on the loopback with no key.</summary>
public sealed class HostApiHardeningTests : IDisposable
{
    private readonly HostApiHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Checkpointed(Dictionary<string, object?> root)
    {
        var orkeon = (Dictionary<string, object?>)root["Orkeon"]!;
        orkeon["Checkpointing"] = new { Enabled = true, Store = "memory" };
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldRefuseAPostWithoutAJsonContentType_WhenTheApiHasNoKey()
    {
        await using var daemon = await _harness.StartAsync(new HeldLlmProvider("unused"));

        using var response = await daemon.SendAsync(
            HttpMethod.Post, "/runs", new { crew = "support", prompt = "hi" },
            configure: r => r.Content = new StringContent("{\"crew\":\"support\",\"prompt\":\"hi\"}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, await daemon.RunCountAsync());
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldRefuseARequestWhoseHostIsNotTheListeners_WhenTheApiHasNoKey()
    {
        await using var daemon = await _harness.StartAsync(new HeldLlmProvider("unused"));

        using var response = await daemon.SendAsync(
            HttpMethod.Get, "/runs", null, configure: r => r.Headers.Host = "rebound.example.test");

        Assert.True(
            response.StatusCode is (HttpStatusCode)421 or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"expected 421, 400 or the listener's own 404, got {(int)response.StatusCode}");
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldCacheReadinessForItsTtl_ThenCheckAgain()
    {
        var store = new FakeStateStore();
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (_, api) => api["ReadinessCacheTtl"] = "01:00:00",
            services: s => s.AddSingleton<IStateStore>(store));

        using var up = await daemon.GetAsync("/readyz");
        store.Throws = true;
        using var cached = await daemon.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldNeverPutACheckDescriptionInTheReadinessBody()
    {
        var store = new FakeStateStore { Throws = true };
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"), services: s => s.AddSingleton<IStateStore>(store));

        using var response = await daemon.GetAsync("/readyz");

        Assert.DoesNotContain("description", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldRefuseAnEventStreamBeyondTheCap_WithAStructured503()
    {
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm, configure: (_, api) => api["MaxEventStreams"] = 1);
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await llm.Started.Task.WaitAsync(Ct);

        using var first = await daemon.GetAsync($"/runs/{runId}/events");
        using var second = await daemon.GetAsync($"/runs/{runId}/events");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        var body = await HostApiHarness.Daemon.JsonAsync(second);
        Assert.Equal("too_many_streams", body["error"]!.GetValue<string>());
        llm.Release();
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldAnswer409WithTheHeldIds_WhenAnAnswerNamesNoQuestionWhileSeveralAreHeld()
    {
        await using var daemon = await _harness.StartAsync(new HeldLlmProvider("unused"), configure: (root, _) => Checkpointed(root));
        var broker = daemon.Host.Services.GetRequiredService<IPendingInputBroker>();
        var a = broker.WaitAsync("run-x", "ask#1", Ct);
        var b = broker.WaitAsync("run-x", "ask#2", Ct);

        using var response = await daemon.PostAsync("/runs/run-x/input", new { answer = "green" });
        var body = await HostApiHarness.Daemon.JsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("request_id_required", body["error"]!.GetValue<string>());
        Assert.Contains("ask#1", body["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("ask#2", body["message"]!.GetValue<string>(), StringComparison.Ordinal);
        broker.TryComplete("run-x", "ask#1", "x");
        broker.TryComplete("run-x", "ask#2", "y");
        await a;
        await b;
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldNameTheRunByTheIdTheApiReturned_WhenTheRunHistoryIsOff()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (root, _) =>
            {
                Checkpointed(root);
                ((Dictionary<string, object?>)root["Orkeon"]!)["RunHistory"] = new { Enabled = false };
            },
            services: s => s.AddScoped<Orkeon.Application.Interfaces.Services.IAgentExecutionService>(
                sp => new AskingAgentExecutionService(sp.GetRequiredService<Orkeon.Application.Interfaces.Ports.IHumanInputProvider>(), "Which colour?")));

        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await Orkeon.Tests.Shared.Timing.Polling.WaitUntilAsync(
            () => Task.FromResult(daemon.Host.Services.GetRequiredService<IPendingInputBroker>().HeldRequestIds(runId).Count == 1),
            TimeSpan.FromSeconds(60));

        using var posted = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "green" });

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public void ShouldAcceptOnlyTheHostsThatNameTheListener()
    {
        var loopback = new HostApiOptions { Prefix = "http://localhost:5190/" };
        var named = new HostApiOptions { Prefix = "http://api.internal:5190/" };
        var wildcard = new HostApiOptions { Prefix = "http://+:5190/" };

        Assert.True(loopback.AcceptsHost("localhost:5190"));
        Assert.True(loopback.AcceptsHost("127.0.0.1:5190"));
        Assert.True(loopback.AcceptsHost("[::1]:5190"));
        Assert.False(loopback.AcceptsHost("rebound.example.test:5190"));
        Assert.False(loopback.AcceptsHost("localhost:6000"));
        Assert.False(loopback.AcceptsHost(null));
        Assert.True(named.AcceptsHost("API.internal:5190"));
        Assert.False(named.AcceptsHost("evil.example:5190"));
        Assert.True(wildcard.AcceptsHost("anything.example:5190"));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldNotTimeTheRunOutWhileItHoldsOnAQuestion_ThenAnswerAndFinish()
    {
        var clock = new Orkeon.Tests.Shared.Timing.ManualTimeProvider();
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (root, _) =>
            {
                Checkpointed(root);
                ((Dictionary<string, object?>)((Dictionary<string, object?>)root["Orkeon"]!)["Host"]!)["RunTimeout"] = "00:01:00";
            },
            services: s =>
            {
                s.AddSingleton<TimeProvider>(clock);
                s.AddScoped<Orkeon.Application.Interfaces.Services.IAgentExecutionService>(
                    sp => new AskingAgentExecutionService(sp.GetRequiredService<Orkeon.Application.Interfaces.Ports.IHumanInputProvider>(), "Which colour?"));
            });
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        var broker = daemon.Host.Services.GetRequiredService<IPendingInputBroker>();
        await Orkeon.Tests.Shared.Timing.Polling.WaitUntilAsync(
            () => Task.FromResult(broker.HeldRequestIds(runId).Count == 1), TimeSpan.FromSeconds(60));

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Single(broker.HeldRequestIds(runId));
        using var posted = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "green" });
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        await daemon.WaitForStatusAsync(runId, "succeeded");
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldAnswer200AlreadyAnswered_WhenTheSameAnswerIsPostedAgainBeforeTheResume()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (root, _) =>
            {
                Checkpointed(root);
                ((Dictionary<string, object?>)root["Orkeon"]!)["HumanInput"] = new { Pause = "Suspend" };
            },
            services: s => s.AddScoped<Orkeon.Application.Interfaces.Services.IAgentExecutionService>(
                sp => new AskingAgentExecutionService(sp.GetRequiredService<Orkeon.Application.Interfaces.Ports.IHumanInputProvider>(), "Which colour?")));
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await daemon.WaitForStatusAsync(runId, "waiting_for_input");

        using var first = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "green" });
        using var again = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "green" });
        var body = await HostApiHarness.Daemon.JsonAsync(again);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("already_answered", body["status"]!.GetValue<string>());
    }
}
