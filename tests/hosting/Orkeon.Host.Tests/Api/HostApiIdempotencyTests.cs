using System.Net;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests.Api;

/// <summary>FWK-14: <c>idempotency_key</c> over HTTP, on the daemon's own pipeline.</summary>
public sealed class HostApiIdempotencyTests : IDisposable
{
    private readonly HostApiHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> RunIdAsync(HttpResponseMessage response) =>
        (await HostApiHarness.Daemon.JsonAsync(response))["run_id"]!.GetValue<string>();

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Replaying_a_post_with_the_same_key_and_request_answers_200_on_the_same_run()
    {
        var llm = new HeldLlmProvider("the answer");
        await using var daemon = await _harness.StartAsync(llm);

        using var first = await daemon.StartRunAsync(idempotencyKey: "k-1");
        var runId = await RunIdAsync(first);
        await daemon.WaitForStatusAsync(runId, "succeeded");
        using var replay = await daemon.StartRunAsync(idempotencyKey: "k-1");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(runId, await RunIdAsync(replay));
        Assert.Equal(1, await daemon.RunCountAsync());
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task The_same_key_with_another_request_is_a_409_and_starts_nothing()
    {
        var llm = new HeldLlmProvider("the answer");
        await using var daemon = await _harness.StartAsync(llm);
        using var first = await daemon.StartRunAsync("one", idempotencyKey: "k-1");
        await daemon.WaitForStatusAsync(await RunIdAsync(first), "succeeded");

        using var other = await daemon.StartRunAsync("two", idempotencyKey: "k-1");
        using var otherVariables = await daemon.StartRunAsync("one", idempotencyKey: "k-1", variables: new { a = "b" });

        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherVariables.StatusCode);
        Assert.Equal(1, await daemon.RunCountAsync());
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Two_posts_with_the_same_key_at_once_start_one_run_and_share_its_id()
    {
        var llm = new HeldLlmProvider("the answer", held: true);
        await using var daemon = await _harness.StartAsync(llm);

        var both = await Task.WhenAll(
            daemon.StartRunAsync(idempotencyKey: "k-1"), daemon.StartRunAsync(idempotencyKey: "k-1"));
        await llm.Started.Task.WaitAsync(Ct);
        var ids = await Task.WhenAll(both.Select(RunIdAsync));
        var statuses = both.Select(r => r.StatusCode).Order().ToArray();
        var running = daemon.Registry.Running.Count;
        llm.Release();
        await daemon.WaitForStatusAsync(ids[0], "succeeded");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Accepted], statuses);
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(1, running);
        Assert.Equal(1, await daemon.RunCountAsync());
        foreach (var response in both)
            response.Dispose();
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_key_survives_a_restart_of_the_daemon_through_the_run_history()
    {
        string runId;
        var firstLlm = new HeldLlmProvider("the answer");
        await using (var first = await _harness.StartAsync(firstLlm))
        {
            using var accepted = await first.StartRunAsync(idempotencyKey: "k-1");
            runId = await RunIdAsync(accepted);
            await first.WaitForStatusAsync(runId, "succeeded");
        }

        var secondLlm = new HeldLlmProvider("the answer");
        await using var second = await _harness.StartAsync(secondLlm);
        using var replay = await second.StartRunAsync(idempotencyKey: "k-1");
        using var other = await second.StartRunAsync("something else", idempotencyKey: "k-1");

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(runId, await RunIdAsync(replay));
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Empty(secondLlm.Prompts);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_run_without_a_key_is_never_deduplicated()
    {
        var llm = new HeldLlmProvider("the answer");
        await using var daemon = await _harness.StartAsync(llm);

        using var one = await daemon.StartRunAsync();
        using var two = await daemon.StartRunAsync();

        Assert.NotEqual(await RunIdAsync(one), await RunIdAsync(two));
    }
}
