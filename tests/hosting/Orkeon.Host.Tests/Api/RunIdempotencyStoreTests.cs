using Orkeon.Host.Api;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests.Api;

/// <summary>FWK-14: the keys of <c>POST /runs</c>, without a daemon. Concurrency is held with gates, never with a delay.</summary>
public sealed class RunIdempotencyStoreTests
{
    private static readonly TimeSpan s_ttl = TimeSpan.FromHours(24);

    private static StartOutcome Started(string id) => StartOutcome.Started(id);

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_second_caller_with_the_same_key_while_the_first_is_still_starting_gets_the_first_run()
    {
        var store = new RunIdempotencyStore(s_ttl, 100);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;

        async Task<StartOutcome> Start()
        {
            Interlocked.Increment(ref starts);
            entered.SetResult();
            await gate.Task;
            return Started("run-1");
        }

        // The first caller is inside its start, parked on the gate: the key is held, the run is not yet known.
        var first = store.StartOnceAsync("k", "h", Start);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = store.StartOnceAsync("k", "h", Start);
        Assert.False(second.IsCompleted);

        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, starts);
        Assert.False(results[0].Replay);
        Assert.True(results[1].Replay);
        Assert.Equal("run-1", results[0].Outcome!.RunId);
        Assert.Equal("run-1", results[1].Outcome!.RunId);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task Many_callers_at_once_start_exactly_one_run()
    {
        var store = new RunIdempotencyStore(s_ttl, 100);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;

        async Task<StartOutcome> Start()
        {
            Interlocked.Increment(ref starts);
            await gate.Task;
            return Started("run-1");
        }

        var callers = Enumerable.Range(0, 32).Select(_ => Task.Run(() => store.StartOnceAsync("k", "h", Start))).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, starts);
        Assert.Equal(1, results.Count(r => !r.Replay));
        Assert.All(results, r => Assert.Equal("run-1", r.Outcome!.RunId));
    }

    [Fact]
    public async Task The_same_key_with_another_hash_is_a_conflict_and_never_starts()
    {
        var store = new RunIdempotencyStore(s_ttl, 100);
        await store.StartOnceAsync("k", "h1", () => Task.FromResult(Started("run-1")));

        var result = await store.StartOnceAsync("k", "h2", () => throw new InvalidOperationException("must not start"));

        Assert.True(result.Conflict);
    }

    [Fact]
    public async Task A_refused_start_is_forgotten_so_the_client_may_retry_the_key()
    {
        var store = new RunIdempotencyStore(s_ttl, 100);
        var refusal = new HostedRunResult(HostedRunOutcome.Busy, null, "busy");

        var first = await store.StartOnceAsync("k", "h", () => Task.FromResult(StartOutcome.Refused(refusal)));
        var retry = await store.StartOnceAsync("k", "h", () => Task.FromResult(Started("run-1")));

        Assert.Equal(StartKind.Refused, first.Outcome!.Kind);
        Assert.False(retry.Replay);
        Assert.Equal("run-1", retry.Outcome!.RunId);
    }

    [Fact]
    public async Task A_start_that_throws_releases_the_key_for_everyone_waiting()
    {
        var store = new RunIdempotencyStore(s_ttl, 100);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = store.StartOnceAsync("k", "h", async () =>
        {
            entered.SetResult();
            await gate.Task;
            throw new InvalidOperationException("boom");
        });
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = store.StartOnceAsync("k", "h", () => Task.FromResult(Started("never")));
        gate.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        var retry = await store.StartOnceAsync("k", "h", () => Task.FromResult(Started("run-2")));
        Assert.Equal("run-2", retry.Outcome!.RunId);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task A_key_is_forgotten_once_its_ttl_has_passed()
    {
        var clock = new ManualTimeProvider();
        var store = new RunIdempotencyStore(TimeSpan.FromHours(1), 100, clock);
        await store.StartOnceAsync("k", "h", () => Task.FromResult(Started("run-1")));

        clock.Advance(TimeSpan.FromMinutes(59));
        var within = await store.StartOnceAsync("k", "h", () => Task.FromResult(Started("run-2")));
        clock.Advance(TimeSpan.FromMinutes(2));
        var after = await store.StartOnceAsync("k", "h", () => Task.FromResult(Started("run-3")));

        Assert.True(within.Replay);
        Assert.False(after.Replay);
        Assert.Equal("run-3", after.Outcome!.RunId);
    }

    [Fact]
    public async Task The_store_holds_no_more_keys_than_its_bound_and_drops_the_oldest_first()
    {
        var clock = new ManualTimeProvider();
        var store = new RunIdempotencyStore(s_ttl, 2, clock);
        foreach (var key in new[] { "a", "b", "c" })
        {
            await store.StartOnceAsync(key, "h", () => Task.FromResult(Started($"run-{key}")));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        var oldest = await store.StartOnceAsync("a", "h", () => Task.FromResult(Started("run-a2")));

        Assert.False(oldest.Replay);
        Assert.True(store.Count <= 2);
    }
}
