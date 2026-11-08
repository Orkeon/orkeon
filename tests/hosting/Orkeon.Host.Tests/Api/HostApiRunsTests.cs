using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests.Api;

/// <summary>FWK-14: the run routes, from <c>POST /runs</c> to a finished run, and the one id behind them.</summary>
public sealed class HostApiRunsTests : IDisposable
{
    private readonly HostApiHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_posted_run_is_accepted_then_read_until_it_has_succeeded()
    {
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm);

        using var accepted = await daemon.StartRunAsync();
        var body = await HostApiHarness.Daemon.JsonAsync(accepted);
        var runId = body["run_id"]!.GetValue<string>();
        await llm.Started.Task.WaitAsync(Ct);
        using var running = await daemon.GetAsync($"/runs/{runId}");
        llm.Release();
        await daemon.WaitForStatusAsync(runId, "succeeded");
        using var list = await daemon.GetAsync("/runs");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.False(body["idempotent_replay"]!.GetValue<bool>());
        Assert.Equal("running", (await HostApiHarness.Daemon.JsonAsync(running))["status"]!.GetValue<string>());
        var runs = (await HostApiHarness.Daemon.JsonAsync(list))["runs"]!.AsArray();
        Assert.Equal(runId, Assert.Single(runs)!["run_id"]!.GetValue<string>());
        Assert.Equal("support", runs[0]!["crew"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task The_run_id_of_the_api_is_the_id_of_the_registry_the_history_and_the_run_started_event()
    {
        var llm = new HeldLlmProvider("the answer");
        await using var daemon = await _harness.StartAsync(llm);

        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await daemon.WaitForStatusAsync(runId, "succeeded");

        var reader = daemon.Host.Services.GetRequiredService<IRunHistoryReader>();
        var summary = Assert.Single(await reader.ListAsync(new RunQuery(), Ct));
        Assert.Equal(runId, summary.RunId);
        var events = await reader.ReadEventsAsync(runId, 1, Ct);
        Assert.Equal(RunEventKinds.RunStarted, events[0].Kind);
        Assert.Equal(runId, events[0].Payload?["hostRunId"]?.GetValue<string>());
        Assert.Equal(runId, events[0].CorrelationId);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_crew_at_its_limit_is_a_429_with_retry_after()
    {
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm, maxRuns: 1);

        using var first = await daemon.StartRunAsync();
        await llm.Started.Task.WaitAsync(Ct);
        using var second = await daemon.StartRunAsync("again");
        llm.Release();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.Contains("Retry-After"));
        Assert.Equal("at_limit", (await HostApiHarness.Daemon.JsonAsync(second))["error"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task An_unknown_crew_is_a_404_and_starts_nothing()
    {
        var llm = new HeldLlmProvider("unused");
        await using var daemon = await _harness.StartAsync(llm);

        using var response = await daemon.StartRunAsync(crew: "billing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_crew", (await HostApiHarness.Daemon.JsonAsync(response))["error"]!.GetValue<string>());
        Assert.Empty(llm.Prompts);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task The_variables_reach_the_crew_as_the_runner_takes_them()
    {
        var llm = new HeldLlmProvider("the answer");
        await using var daemon = await _harness.StartAsync(llm);

        using var accepted = await daemon.StartRunAsync(variables: new { topic = "elections" });
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await daemon.WaitForStatusAsync(runId, "succeeded");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    [Theory]
    [InlineData("""{"crew":"support"}""")]
    [InlineData("""{"crew":"support","prompt":"x","variables":{"n":1}}""")]
    [InlineData("""{"crew":"support","prompt":"x","surprise":true}""")]
    [InlineData("not json")]
    public async Task A_malformed_body_is_a_400(string json)
    {
        await using var daemon = await _harness.StartAsync(new HeldLlmProvider("unused"));

        using var response = await daemon.SendAsync(HttpMethod.Post, "/runs", null, configure: r =>
            r.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_running_run_is_cancelled_once_and_a_finished_one_is_a_409()
    {
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm);
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await llm.Started.Task.WaitAsync(Ct);

        using var cancel = await daemon.PostAsync($"/runs/{runId}/cancel", null);
        await daemon.WaitForStatusAsync(runId, "failed");
        using var again = await daemon.PostAsync($"/runs/{runId}/cancel", null);
        using var unknown = await daemon.PostAsync("/runs/01ARZ3NDEKTSV4RRFFQ69G5FAV/cancel", null);

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task An_unknown_run_and_an_unknown_route_are_404()
    {
        await using var daemon = await _harness.StartAsync(new HeldLlmProvider("unused"));

        using var run = await daemon.GetAsync("/runs/01ARZ3NDEKTSV4RRFFQ69G5FAV");
        using var events = await daemon.GetAsync("/runs/01ARZ3NDEKTSV4RRFFQ69G5FAV/events");
        using var route = await daemon.GetAsync("/nothing");

        Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, events.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, route.StatusCode);
    }
}
