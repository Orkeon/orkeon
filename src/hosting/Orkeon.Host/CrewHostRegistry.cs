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

    /// <summary>
    /// True when a person (or the drain) asked this run to stop — as opposed to its own
    /// deadline firing. The two collapse into one cancelled token, and the runner needs the
    /// difference to say "stopped" versus "timed out": the single most common question about
    /// a hosted run.
    /// </summary>
    public bool StopRequested { get; set; }
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
    private readonly Lock _admission = new();
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
    public HostedRun? TryStart(string crewName, string origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewName);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var crew = Find(crewName);
        if (crew is null)
            return null;

        // Counted rather than gated by a semaphore: the caller needs to know *now* whether it
        // was accepted, so it can say so, instead of queueing behind an invisible wait. Count
        // and admit under one short lock — count-then-add let two simultaneous callers both
        // read Max-1 and both get in, overshooting the one bound this registry exists for.
        //
        // The run's cancellation source is deliberately NOT linked to any caller token: on
        // SIGTERM the channel's stopping token fires at t=0, and runs linked to it were dead
        // before the shutdown grace period ever started — the drain politely waited for
        // corpses. Stopping a run is the registry's own gesture (RequestStop / the drain).
        lock (_admission)
        {
            var inFlight = _runs.Values.Count(run =>
                string.Equals(run.CrewName, crew.Name, StringComparison.OrdinalIgnoreCase));

            if (inFlight >= crew.Profile.MaxConcurrentRuns)
                return null;

            // A 12-hex-char id can collide once in a blue moon; retrying costs nothing,
            // while returning null here would report the collision to the user as "busy".
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var run = new HostedRun
                {
                    Id = Guid.NewGuid().ToString("N")[..12],
                    CrewName = crew.Name,
                    Origin = origin,
                    StartedAt = _time.GetUtcNow(),
                    Cancellation = new CancellationTokenSource(),
                };

                if (_runs.TryAdd(run.Id, run))
                    return run;

                run.Cancellation.Dispose();
            }

            return null;
        }
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
        if (!_runs.TryGetValue(runId, out var run))
            return false;

        return TryCancel(run);
    }

    /// <summary>Asks every run in flight to stop, and reports how many were asked.</summary>
    public int RequestStopAll()
    {
        var asked = 0;
        foreach (var run in _runs.Values)
        {
            if (TryCancel(run))
                asked++;
        }

        return asked;
    }

    /// <summary>
    /// Cancels a run's source, racing its own completion gracefully: Finish disposes the
    /// source, and a Stop pressed in the same instant a run ends must read as "already
    /// finished", not throw ObjectDisposedException out of a button handler.
    /// </summary>
    private static bool TryCancel(HostedRun run)
    {
        try
        {
            if (run.Cancellation.IsCancellationRequested)
                return false;

            run.StopRequested = true;
            run.Cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (AggregateException)
        {
            // Cancel() runs registered callbacks synchronously and wraps their failures. The
            // stop was still delivered; a callback's tantrum must not escape into a button
            // handler or take the drain — and with it the host's clean exit — down.
            return true;
        }
    }
}
