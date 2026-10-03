using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// A router whose work has steps: it reports <see cref="Lines"/> through the progress it is given,
/// then answers Completed. It keeps that progress, so a test can report through it once the
/// answer is out — what a run's callback racing the end of the run does.
/// </summary>
internal sealed class ProgressReportingTaskRouter(params string[] lines) : IA2ATaskRouter
{
    public IReadOnlyList<string> Lines { get; } = lines;

    /// <summary>The progress of the last task, as the server passed it; null for <c>send</c>.</summary>
    public IProgress<string>? LastProgress { get; private set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<AgentSkill>> GetSkillsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AgentSkill>>([]);

    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, IProgress<string>? progress, CancellationToken ct = default)
    {
        Calls++;
        LastProgress = progress;
        foreach (var line in Lines)
            progress?.Report(line);

        return Task.FromResult(new A2ATaskResponse
        {
            TaskId = request.Id,
            Status = A2ATaskStatus.Completed,
            Output = "the report",
        });
    }
}

/// <summary>
/// GAP-35 — a peer following a task with <c>sendSubscribe</c> reads its progress: each line the
/// router reports is a <c>Working</c> update carrying it as its <c>message</c>, in order, before
/// the final state. One writer drains one queue, so nothing reaches the peer after the final
/// state. <c>send</c> answers as before, and its router is given no progress to report to.
/// </summary>
public sealed class A2AServerProgressTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StringContent TaskBody(string id) => new(
        JsonSerializer.Serialize(new A2ATaskRequest { Id = id, SkillId = "veille", Input = "What changed?" }),
        Encoding.UTF8,
        "application/json");

    /// <summary>The <c>data:</c> payloads of an SSE stream, in order, <c>[DONE]</c> included.</summary>
    private static async Task<IReadOnlyList<string>> ReadEventsAsync(HttpResponseMessage response)
    {
        var events = new List<string>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                events.Add(line["data: ".Length..]);
        }

        return events;
    }

    [Fact]
    public async Task SendSubscribe_streams_each_reported_line_as_a_Working_update_before_the_final_state()
    {
        var router = new ProgressReportingTaskRouter("Running 'veille'…", "✔ Analyst — step 1 done");
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router), Ct);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var body = TaskBody("watch-1");
            using var response = await http.PostAsync(
                new Uri($"{LoopbackPorts.Host}:{port}/a2a/tasks/sendSubscribe"), body, Ct);
            var events = await ReadEventsAsync(response);

            Assert.Equal(5, events.Count);
            Assert.Equal("[DONE]", events[^1]);
            var updates = events.Take(4)
                .Select(json => JsonSerializer.Deserialize<A2ATaskUpdate>(json, s_json)!)
                .ToList();

            Assert.Equal(
                [A2ATaskStatus.Working, A2ATaskStatus.Working, A2ATaskStatus.Working, A2ATaskStatus.Completed],
                updates.Select(u => u.Status));
            Assert.Equal(
                [null, "Running 'veille'…", "✔ Analyst — step 1 done", null],
                updates.Select(u => u.Message));
            Assert.All(updates, u => Assert.Equal("watch-1", u.TaskId));
            // The output stays the output: the progress never mixes into it.
            Assert.Equal([null, null, null, "the report"], updates.Select(u => u.PartialOutput));

            // A line reported once the answer is out goes nowhere — the stream already closed on
            // the final state, and reporting must not throw at whoever reports it late.
            router.LastProgress!.Report("too late");
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Send_answers_as_before_and_gives_the_router_no_progress_to_report_to()
    {
        var router = new ProgressReportingTaskRouter("Running 'veille'…");
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router), Ct);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var body = TaskBody("watch-2");
            using var response = await http.PostAsync(
                new Uri($"{LoopbackPorts.Host}:{port}/a2a/tasks/send"), body, Ct);
            var answer = JsonSerializer.Deserialize<A2ATaskResponse>(await response.Content.ReadAsStringAsync(Ct), s_json)!;

            Assert.Equal(A2ATaskStatus.Completed, answer.Status);
            Assert.Equal("the report", answer.Output);
            Assert.Equal(1, router.Calls);
            Assert.Null(router.LastProgress);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }
}
