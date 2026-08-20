using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Orkeon.Host;

/// <summary>What the registry knows about one run in flight.</summary>
internal sealed record HostedRun
{
    /// <summary>Identifier the operator and the channels use to name this run.</summary>
    public required string Id { get; init; }

    /// <summary>The crew it runs.</summary>
    public required string CrewName { get; init; }

    /// <summary>Where the request came from — a Discord thread, a console, a test.</summary>
    public required string Origin { get; init; }

    /// <summary>When it started, in UTC.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Cancels this run and nothing else.</summary>
    public required CancellationTokenSource Cancellation { get; init; }
}

/// <summary>
/// Knows which crews the service hosts and which of their runs are in flight (GATE-02).
/// <para>
/// The concurrency bound is the point. A daemon that accepts every request that arrives is a
/// daemon that dies under its first burst, and a chat channel makes bursts trivial — one
/// enthusiastic user, ten threads. Refusing the eleventh with a clear answer is a feature.
/// </para>
/// </summary>
internal sealed class CrewHostRegistry
{
    private readonly ConcurrentDictionary<string, HostedRun> _runs = new(StringComparer.Ordinal);
    private readonly OrkeonHostOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Builds the registry over the configured crews.</summary>
    public CrewHostRegistry(IOptions<OrkeonHostOptions> options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The crews the configuration declared, in declaration order.</summary>
    public IReadOnlyList<HostedCrewOptions> Crews => _options.Crews;

    /// <summary>Runs currently in flight, newest last.</summary>
    public IReadOnlyList<HostedRun> Running =>
        [.. _runs.Values.OrderBy(run => run.StartedAt)];

    /// <summary>Finds a hosted crew by name, or null when the configuration declares none such.</summary>
    public HostedCrewOptions? Find(string crewName) =>
        _options.Crews.FirstOrDefault(c => string.Equals(c.Name, crewName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reserves a slot for a run of <paramref name="crewName"/>, or returns null when the crew
    /// is unknown or already at its concurrency limit. Returning null rather than throwing is
    /// deliberate: "we are busy" is an answer a channel can relay, not an incident.
    /// </summary>
    public HostedRun? TryStart(string crewName, string origin, CancellationToken linkedTo = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewName);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var crew = Find(crewName);
        if (crew is null)
            return null;

        // Counted rather than gated by a semaphore: the caller needs to know *now* whether it
        // was accepted, so it can say so, instead of queueing behind an invisible wait.
        var inFlight = _runs.Values.Count(run =>
            string.Equals(run.CrewName, crew.Name, StringComparison.OrdinalIgnoreCase));

        if (inFlight >= crew.Profile.MaxConcurrentRuns)
            return null;

        var run = new HostedRun
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            CrewName = crew.Name,
            Origin = origin,
            StartedAt = _time.GetUtcNow(),
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(linkedTo),
        };

        return _runs.TryAdd(run.Id, run) ? run : null;
    }

    /// <summary>Releases a run's slot and disposes its cancellation source.</summary>
    public void Finish(string runId)
    {
        if (_runs.TryRemove(runId, out var run))
            run.Cancellation.Dispose();
    }

    /// <summary>
    /// Asks one run to stop. False when no such run is in flight — which is what a user gets
    /// for stopping something that already finished, and not an error.
    /// </summary>
    public bool RequestStop(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run) || run.Cancellation.IsCancellationRequested)
            return false;

        run.Cancellation.Cancel();
        return true;
    }

    /// <summary>Asks every run in flight to stop, and reports how many were asked.</summary>
    public int RequestStopAll()
    {
        var asked = 0;
        foreach (var run in _runs.Values)
        {
            if (run.Cancellation.IsCancellationRequested)
                continue;

            run.Cancellation.Cancel();
            asked++;
        }

        return asked;
    }
}
