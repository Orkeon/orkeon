using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Timing;
using System.Text.Json.Nodes;
using Orkeon.Constants.Protocol;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests.Api;

/// <summary>FWK-14: <c>GET /runs/{id}/events</c>, as SSE with <c>id: seq</c> and as NDJSON, and the resume points.</summary>
public sealed class HostApiEventsStreamTests : IDisposable
{
    private readonly HostApiHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Sse(HttpRequestMessage request) => request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

    private async Task<(HostApiHarness.Daemon Daemon, string RunId)> FinishedRunAsync()
    {
        var daemon = await _harness.StartAsync(new HeldLlmProvider("the answer"));
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await daemon.WaitForStatusAsync(runId, "succeeded");
        return (daemon, runId);
    }

    /// <summary>The (id, event) frames of an SSE body, up to the end of the stream.</summary>
    private static async Task<List<(int Id, string Event, JsonObject Data)>> ReadSseAsync(HttpResponseMessage response)
    {
        var frames = new List<(int, string, JsonObject)>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        int? id = null;
        string? kind = null;
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (line.StartsWith("id: ", StringComparison.Ordinal))
                id = int.Parse(line[4..], System.Globalization.CultureInfo.InvariantCulture);
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
                kind = line[7..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
                frames.Add((id!.Value, kind!, JsonNode.Parse(line[6..])!.AsObject()));
        }

        return frames;
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_finished_run_streams_every_event_as_sse_with_its_seq_as_the_event_id()
    {
        var (daemon, runId) = await FinishedRunAsync();
        await using var _ = daemon;

        using var response = await daemon.SendAsync(HttpMethod.Get, $"/runs/{runId}/events", null, configure: Sse);
        var frames = await ReadSseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/event-stream", response.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
        Assert.Equal(RunEventKinds.RunStarted, frames[0].Event);
        Assert.Equal(RunEventKinds.RunFinished, frames[^1].Event);
        Assert.Equal(Enumerable.Range(1, frames.Count), frames.Select(f => f.Id));
        Assert.All(frames, f => Assert.Equal(f.Id, f.Data["seq"]!.GetValue<int>()));
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_client_resumes_after_the_last_event_id_it_received()
    {
        var (daemon, runId) = await FinishedRunAsync();
        await using var _ = daemon;

        using var response = await daemon.SendAsync(HttpMethod.Get, $"/runs/{runId}/events", null, configure: r =>
        {
            Sse(r);
            r.Headers.TryAddWithoutValidation("Last-Event-ID", "2");
        });
        var frames = await ReadSseAsync(response);

        Assert.Equal(3, frames[0].Id);
        Assert.Equal(RunEventKinds.RunFinished, frames[^1].Event);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Without_an_sse_accept_the_stream_is_ndjson_and_from_is_inclusive()
    {
        var (daemon, runId) = await FinishedRunAsync();
        await using var _ = daemon;

        using var response = await daemon.GetAsync($"/runs/{runId}/events?from=2");
        var lines = (await response.Content.ReadAsStringAsync(Ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("application/x-ndjson", response.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, JsonNode.Parse(lines[0])!["seq"]!.GetValue<int>());
        Assert.Equal(RunEventKinds.RunFinished, JsonNode.Parse(lines[^1])!["kind"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_quiet_stream_gets_a_keep_alive_comment_each_interval_of_the_clock()
    {
        var clock = new ManualTimeProvider();
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm, services: s => s.AddSingleton<TimeProvider>(clock));
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await llm.Started.Task.WaitAsync(Ct);

        using var response = await daemon.SendAsync(HttpMethod.Get, $"/runs/{runId}/events", null, configure: Sse);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        var keepAlive = Task.Run(async () =>
        {
            while (await reader.ReadLineAsync(Ct) is { } line)
            {
                if (line == ": keep-alive")
                    return true;
            }

            return false;
        }, Ct);

        // The server waits on the fake clock between two reads of the history: each advance is one more poll.
        while (!keepAlive.IsCompleted)
        {
            await Polling.WaitUntilAsync(() => clock.PendingTimers > 0 || keepAlive.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(await keepAlive);
        llm.Release();
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_client_that_goes_away_ends_its_stream_quietly_at_debug_with_no_error_and_no_500()
    {
        var clock = new ManualTimeProvider();
        using var logs = new RecordingLoggerProvider();
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm, services: s => s.AddSingleton<TimeProvider>(clock), logs: logs);
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await llm.Started.Task.WaitAsync(Ct);

        var response = await daemon.SendAsync(HttpMethod.Get, $"/runs/{runId}/events", null, configure: Sse);
        using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct)))
            Assert.StartsWith("id: 1", await reader.ReadLineAsync(Ct), StringComparison.Ordinal);
        response.Dispose();

        // The next keep-alive writes to a closed connection: the stream must end, and say so at Debug only.
        bool Ended() => logs.Logger.HasEntry(e => e.Message.Contains("went away", StringComparison.Ordinal))
            || logs.Logger.HasEntry(e => e.Level >= LogLevel.Warning && e.Message.Contains("Host API", StringComparison.Ordinal));
        while (!Ended())
        {
            await Polling.WaitUntilAsync(() => clock.PendingTimers > 0 || Ended());
            clock.Advance(TimeSpan.FromSeconds(16));
        }

        Assert.All(
            logs.Logger.Entries.Where(e => e.Message.Contains("Host API", StringComparison.Ordinal)),
            e => Assert.True(e.Level < LogLevel.Warning, e.Message));
        Assert.Contains(logs.Logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("went away", StringComparison.Ordinal));
        llm.Release();
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_live_run_is_followed_until_run_finished()
    {
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm);
        using var accepted = await daemon.StartRunAsync();
        var runId = (await HostApiHarness.Daemon.JsonAsync(accepted))["run_id"]!.GetValue<string>();
        await llm.Started.Task.WaitAsync(Ct);

        using var response = await daemon.SendAsync(HttpMethod.Get, $"/runs/{runId}/events", null);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        var first = JsonNode.Parse((await reader.ReadLineAsync(Ct))!)!;
        llm.Release();
        var kinds = new List<string> { first["kind"]!.GetValue<string>() };
        while (await reader.ReadLineAsync(Ct) is { } line)
            kinds.Add(JsonNode.Parse(line)!["kind"]!.GetValue<string>());

        Assert.Equal(RunEventKinds.RunStarted, kinds[0]);
        Assert.Equal(RunEventKinds.RunFinished, kinds[^1]);
    }
}
