using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Host.Api;

/// <summary>How an attempt to start a run ended.</summary>
internal enum StartKind
{
    /// <summary>The run was admitted; its id is known.</summary>
    Started,

    /// <summary>The runner refused it (unknown crew, at the limit, the host is stopping).</summary>
    Refused,
}

/// <summary>The outcome of starting a run, as the API turns it into a response.</summary>
/// <param name="Kind">Started or refused.</param>
/// <param name="RunId">The run's id when it started.</param>
/// <param name="Refusal">The runner's answer when it refused.</param>
internal sealed record StartOutcome(StartKind Kind, string? RunId, HostedRunResult? Refusal)
{
    public static StartOutcome Started(string runId) => new(StartKind.Started, runId, null);

    public static StartOutcome Refused(HostedRunResult refusal) => new(StartKind.Refused, null, refusal);
}

/// <summary>What <see cref="RunIdempotencyStore.StartOnceAsync"/> decided.</summary>
/// <param name="Conflict">The key was seen with another request: 409.</param>
/// <param name="Replay">The key was seen with the same request: the first run's outcome, not a new run.</param>
/// <param name="Outcome">The outcome (null on a conflict).</param>
internal sealed record IdempotentStart(bool Conflict, bool Replay, StartOutcome? Outcome);

/// <summary>
/// The idempotency keys of <c>POST /runs</c> (FWK-14): a key maps to the hash of the request that carried it and to the
/// run that request started.
/// <para>
/// **Concurrency.** The first caller of a key registers a pending entry under the lock, then starts the run outside it;
/// a caller that arrives meanwhile with the same key finds the pending entry and awaits it. One run starts, and the
/// second caller gets the first's run id. A refused attempt is forgotten, so the client may retry the key later.
/// </para>
/// <para>
/// **Lifetime.** Keys live in memory for <c>IdempotencyTtl</c> (24 h by default), at most <c>IdempotencyMaxKeys</c>.
/// Each is also written into the <c>run.started</c> event of its run (the run history), and <see cref="HydrateAsync"/>
/// reads them back when the API starts: across a restart a key is honoured for as long as the history still holds its run
/// and the TTL has not passed. With the history off, a restart forgets every key.
/// </para>
/// </summary>
internal sealed class RunIdempotencyStore
{
    private readonly Lock _gate;
    private readonly Dictionary<string, Entry> _entries;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly int _maxKeys;

    /// <summary>Builds a store keeping at most <paramref name="maxKeys"/> keys, each for <paramref name="ttl"/>.</summary>
    public RunIdempotencyStore(TimeSpan ttl, int maxKeys, TimeProvider? time = null)
    {
        _gate = new Lock();
        _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        _time = time ?? TimeProvider.System;
        _ttl = ttl;
        _maxKeys = maxKeys;
    }

    /// <summary>The number of keys held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>
    /// Starts a run once per key: <paramref name="start"/> runs for the first caller only; every other caller with the
    /// same key and the same <paramref name="hash"/> gets that outcome, and one with another hash gets a conflict.
    /// </summary>
    /// <param name="key">The client's idempotency key.</param>
    /// <param name="hash">The hash of the request.</param>
    /// <param name="start">Starts the run and reports how it ended.</param>
    public async Task<IdempotentStart> StartOnceAsync(string key, string hash, Func<Task<StartOutcome>> start)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(start);

        TaskCompletionSource<StartOutcome>? ownership = null;
        Task<StartOutcome>? replay = null;
        lock (_gate)
        {
            Purge();
            if (_entries.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.Hash, hash, StringComparison.Ordinal))
                    return new IdempotentStart(Conflict: true, Replay: false, null);

                replay = existing.Outcome;
            }
            else
            {
                ownership = new TaskCompletionSource<StartOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                _entries[key] = new Entry(hash, ownership.Task, _time.GetUtcNow());
            }
        }

        // A caller that arrived while the first was still starting waits for it, outside the lock.
        if (replay is not null)
            return new IdempotentStart(false, true, await replay.ConfigureAwait(false));

        StartOutcome outcome;
        try
        {
            outcome = await start().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Forget(key);
            ownership!.SetException(ex);
            throw;
        }

        if (outcome.Kind != StartKind.Started)
            Forget(key);
        ownership!.SetResult(outcome);
        return new IdempotentStart(false, false, outcome);
    }

    /// <summary>Records a key found in the history: a run that started under it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="hash">The hash of the request that carried it.</param>
    /// <param name="runId">The run it started.</param>
    /// <param name="startedAt">When the run started: the key lives from then.</param>
    public void Remember(string key, string hash, string runId, DateTimeOffset startedAt)
    {
        lock (_gate)
            _entries.TryAdd(key, new Entry(hash, Task.FromResult(StartOutcome.Started(runId)), startedAt));
    }

    /// <summary>
    /// Reads the keys back from the run history: the runs that started within the TTL, newest first, whose
    /// <c>run.started</c> carries a key.
    /// </summary>
    /// <param name="history">The history reader.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many keys were recovered.</returns>
    public async Task<int> HydrateAsync(IRunHistoryReader history, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        var horizon = _time.GetUtcNow() - _ttl;
        var recovered = 0;
        var runs = await history.ListAsync(new RunQuery { Limit = Math.Min(_maxKeys, 1000) }, ct).ConfigureAwait(false);
        foreach (var run in runs.Where(r => r.StartedAt >= horizon))
        {
            var events = await history.ReadEventsAsync(run.RunId, 1, 1, ct).ConfigureAwait(false);
            var started = events.FirstOrDefault(e => e.Kind == RunEventKinds.RunStarted);
            var key = started?.Payload?["idempotencyKey"]?.GetValue<string>();
            var hash = started?.Payload?["requestHash"]?.GetValue<string>();
            if (key is null || hash is null)
                continue;

            Remember(key, hash, run.RunId, run.StartedAt);
            recovered++;
        }

        return recovered;
    }

    private void Forget(string key)
    {
        lock (_gate)
            _entries.Remove(key);
    }

    // Under the lock: drops what outlived the TTL, then the oldest finished entries beyond the bound.
    private void Purge()
    {
        var horizon = _time.GetUtcNow() - _ttl;
        foreach (var expired in _entries.Where(e => e.Value.Outcome.IsCompleted && e.Value.At < horizon).Select(e => e.Key).ToList())
            _entries.Remove(expired);

        if (_entries.Count < _maxKeys)
            return;

        foreach (var oldest in _entries.Where(e => e.Value.Outcome.IsCompleted)
                     .OrderBy(e => e.Value.At).Take(_entries.Count - _maxKeys + 1).Select(e => e.Key).ToList())
        {
            _entries.Remove(oldest);
        }
    }

    private sealed record Entry(string Hash, Task<StartOutcome> Outcome, DateTimeOffset At);
}
