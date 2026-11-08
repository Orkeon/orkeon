using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests.Api;

/// <summary>
/// FWK-23: <c>POST /runs/{id}/input</c>. A hosted run that asks a human holds on the question, the answer is posted
/// and the run goes on; with the policy set to suspend, the run ends <c>waiting_for_input</c> and the answer is recorded for a resume.
/// </summary>
public sealed class HostApiInputTests : IDisposable
{
    private static readonly string[] s_apiKey = ["ApiKey"];
    private static readonly string[] s_apiKeyName = ["API_KEY"];

    private readonly HostApiHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Checkpointed(Dictionary<string, object?> root, string? pause)
    {
        var orkeon = (Dictionary<string, object?>)root["Orkeon"]!;
        orkeon["Checkpointing"] = new { Enabled = true, Store = "memory" };
        if (pause is not null)
            orkeon["HumanInput"] = new { Pause = pause };
    }

    private static void Asks(IServiceCollection services) =>
        services.AddScoped<IAgentExecutionService>(sp => new AskingAgentExecutionService(sp.GetRequiredService<IHumanInputProvider>(), "Which colour?"));

    private static async Task<JsonObject> PendingAsync(HostApiHarness.Daemon daemon, string runId)
    {
        JsonObject? pending = null;
        await Polling.WaitUntilAsync(async () =>
        {
            using var response = await daemon.GetAsync($"/runs/{runId}");
            pending = (await HostApiHarness.Daemon.JsonAsync(response))["pending_input"]?.AsObject();
            return pending is not null;
        }, TimeSpan.FromSeconds(60));
        return pending!;
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task A_held_run_goes_on_and_completes_when_its_question_is_answered()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"), configure: (root, _) => Checkpointed(root, null), services: Asks);

        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        var pending = await PendingAsync(daemon, runId);
        using var posted = await daemon.PostAsync($"/runs/{runId}/input", new { request_id = pending["request_id"]!.GetValue<string>(), answer = "green" });
        await daemon.WaitForStatusAsync(runId, "succeeded");

        Assert.Equal("Which colour?", pending["prompt"]!.GetValue<string>());
        Assert.Equal("text", pending["kind"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal("delivered", (await HostApiHarness.Daemon.JsonAsync(posted))["status"]!.GetValue<string>());
        var events = await daemon.Host.Services.GetRequiredService<IRunHistoryReader>().ReadEventsAsync(runId, 1, Ct);
        var needed = Assert.Single(events, e => e.Kind == RunEventKinds.InputNeeded);
        Assert.Equal(pending["request_id"]!.GetValue<string>(), needed.Payload?["requestId"]?.GetValue<string>());
        Assert.Contains(events, e => e.Kind == RunEventKinds.InputGiven);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task A_secret_in_the_prompt_is_masked_in_the_run_reading_and_kept_for_the_resume()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (root, _) => Checkpointed(root, "Suspend"),
            services: s => s.AddScoped<IAgentExecutionService>(sp => new AskingAgentExecutionService(sp.GetRequiredService<IHumanInputProvider>(), "Use password=hunter2hunter2 here?")));

        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await daemon.WaitForStatusAsync(runId, "waiting_for_input");
        var pending = await PendingAsync(daemon, runId);

        var prompt = pending["prompt"]!.GetValue<string>();
        Assert.DoesNotContain("hunter2hunter2", prompt, StringComparison.Ordinal);
        Assert.Contains("here?", prompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task The_first_answer_wins_and_a_wrong_question_is_refused()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"), configure: (root, _) => Checkpointed(root, null), services: Asks);
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        var pending = await PendingAsync(daemon, runId);

        using var wrong = await daemon.PostAsync($"/runs/{runId}/input", new { request_id = "other#1.1", answer = "x" });
        using var missing = await daemon.PostAsync($"/runs/{runId}/input", new { request_id = pending["request_id"]!.GetValue<string>() });
        using var first = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "green" });
        await daemon.WaitForStatusAsync(runId, "succeeded");
        using var second = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "red" });

        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Equal("request_mismatch", (await HostApiHarness.Daemon.JsonAsync(wrong))["error"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("no_pending_input", (await HostApiHarness.Daemon.JsonAsync(second))["error"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task A_second_answer_is_refused_while_the_run_has_not_yet_recorded_the_first()
    {
        // PR review HOST-1: the run is handed the first answer but records it only once it wakes; until then the session
        // still shows the question open. A different answer must not be recorded over the one the run goes on with.
        var broker = new GatedPendingInputBroker();
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (root, _) => Checkpointed(root, null),
            services: s =>
            {
                Asks(s);
                s.AddSingleton<Orkeon.Application.Interfaces.Checkpointing.IPendingInputBroker>(broker);
            });
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        var requestId = (await PendingAsync(daemon, runId))["request_id"]!.GetValue<string>();

        using var first = await daemon.PostAsync($"/runs/{runId}/input", new { request_id = requestId, answer = "allow" });
        using var other = await daemon.PostAsync($"/runs/{runId}/input", new { request_id = requestId, answer = "deny" });
        using var unnamed = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "deny" });
        using var again = await daemon.PostAsync($"/runs/{runId}/input", new { request_id = requestId, answer = "allow" });
        broker.Wake();
        await daemon.WaitForStatusAsync(runId, "succeeded");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("delivered", (await HostApiHarness.Daemon.JsonAsync(first))["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal("no_pending_input", (await HostApiHarness.Daemon.JsonAsync(other))["error"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, unnamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("already_answered", (await HostApiHarness.Daemon.JsonAsync(again))["status"]!.GetValue<string>());
        var session = await daemon.Host.Services.GetRequiredService<Orkeon.Application.Interfaces.Checkpointing.IStateStore>().GetAsync(runId, Ct);
        Assert.Equal("allow", session?.AnsweredInputs[requestId]);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task A_suspended_run_reads_waiting_for_input_and_records_the_answer()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"), configure: (root, _) => Checkpointed(root, "Suspend"), services: Asks);

        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await daemon.WaitForStatusAsync(runId, "waiting_for_input");
        var pending = await PendingAsync(daemon, runId);
        using var posted = await daemon.PostAsync($"/runs/{runId}/input", new { request_id = pending["request_id"]!.GetValue<string>(), answer = "green" });
        using var again = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "red" });

        Assert.Equal(HttpStatusCode.Accepted, posted.StatusCode);
        Assert.Equal("recorded", (await HostApiHarness.Daemon.JsonAsync(posted))["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var events = await daemon.Host.Services.GetRequiredService<IRunHistoryReader>().ReadEventsAsync(runId, 1, Ct);
        var finished = events.Last(e => e.Kind == RunEventKinds.RunFinished);
        Assert.Equal("waiting_for_input", finished.Payload?["status"]?.GetValue<string>());
        Assert.Equal(75, finished.Payload?["exitCode"]?.GetValue<int>());
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task The_input_route_needs_the_same_authorization_as_the_other_run_routes()
    {
        await using var daemon = await _harness.StartAsync(
            new HeldLlmProvider("unused"),
            configure: (root, api) =>
            {
                Checkpointed(root, null);
                api["Security"] = new { AllowedAuthSchemes = s_apiKey, ApiKeySecretNames = s_apiKeyName };
                root["Secrets"] = new { API_KEY = "secret" };
            });

        using var anonymous = await daemon.PostAsync("/runs/01HZZZZZZZZZZZZZZZZZZZZZZZ/input", new { answer = "x" });
        using var authorised = await daemon.PostAsync("/runs/01HZZZZZZZZZZZZZZZZZZZZZZZ/input", new { answer = "x" }, authorization: "ApiKey secret");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, authorised.StatusCode);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task An_answer_is_refused_as_input_unavailable_when_the_host_keeps_no_checkpoints()
    {
        await using var daemon = await _harness.StartAsync(new HeldLlmProvider("unused"), services: Asks);
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();

        using var posted = await daemon.PostAsync($"/runs/{runId}/input", new { answer = "green" });

        Assert.Equal(HttpStatusCode.Conflict, posted.StatusCode);
        Assert.Equal("input_unavailable", (await HostApiHarness.Daemon.JsonAsync(posted))["error"]!.GetValue<string>());
    }
}
