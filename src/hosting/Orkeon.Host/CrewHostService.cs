using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orkeon.Host;

/// <summary>
/// The service's lifetime (GATE-01): it starts, it says what it hosts, it stays up, and it
/// stops without abandoning a run mid-sentence.
/// <para>
/// The daemon does not run crews on its own — it waits to be asked. rc.2 hosts no scheduler,
/// deliberately, and the documentation must not imply that a scheduled crew is possible. What
/// it does own is the lifetime and the graceful stop: a run in flight gets a grace period to
/// finish before the process leaves.
/// </para>
/// </summary>
internal sealed partial class CrewHostService : BackgroundService
{
    private readonly CrewHostRegistry _registry;
    private readonly OrkeonHostOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<CrewHostService> _logger;

    /// <summary>Builds the service over the registry and the host's lifetime.</summary>
    public CrewHostService(
        CrewHostRegistry registry,
        IOptions<OrkeonHostOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<CrewHostService> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_registry.Crews.Count == 0)
        {
            // A daemon hosting nothing is a configuration mistake, not a state to sit in
            // quietly: systemd would report it as healthy forever.
            LogNoCrewsConfigured(OrkeonHostOptions.SectionName);
            _lifetime.StopApplication();
            return;
        }

        foreach (var crew in _registry.Crews)
            LogHostingCrew(crew.Name, crew.Path, crew.Profile.MaxConcurrentRuns);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: this is how a stop arrives.
        }

        await DrainAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gives runs in flight their grace period, then asks them to stop. Killing them the
    /// instant a signal arrives would lose work a few seconds from being finished; waiting
    /// forever would earn a SIGKILL, which loses it anyway and less politely.
    /// </summary>
    private async Task DrainAsync()
    {
        var deadline = DateTimeOffset.UtcNow + _options.ShutdownGracePeriod;
        var inFlight = _registry.Running.Count;
        if (inFlight == 0)
            return;

        LogDraining(inFlight, _options.ShutdownGracePeriod);

        while (_registry.Running.Count > 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);

        var stopped = _registry.RequestStopAll();
        if (stopped > 0)
            LogStoppedRemaining(stopped);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "No hosted crew is configured under '{Section}:Crews'; the service has nothing to do and is stopping.")]
    private partial void LogNoCrewsConfigured(string section);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hosting crew '{CrewName}' from {Path} (up to {MaxRuns} concurrent run(s))")]
    private partial void LogHostingCrew(string crewName, string path, int maxRuns);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping: {InFlight} run(s) in flight, waiting up to {Grace}")]
    private partial void LogDraining(int inFlight, TimeSpan grace);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Grace period elapsed: asked {Stopped} run(s) to stop")]
    private partial void LogStoppedRemaining(int stopped);
}

/// <summary>
/// Reports whether the service is doing what it was configured to do — hosting crews, and not
/// wedged at its concurrency ceiling.
/// </summary>
internal sealed class CrewHostHealthCheck : IHealthCheck
{
    private readonly CrewHostRegistry _registry;

    /// <summary>Builds the check over the registry.</summary>
    public CrewHostHealthCheck(CrewHostRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_registry.Crews.Count == 0)
            return Task.FromResult(HealthCheckResult.Unhealthy("No hosted crew is configured."));

        var running = _registry.Running.Count;
        var capacity = _registry.Crews.Sum(crew => crew.Profile.MaxConcurrentRuns);

        // Full is not broken — it is a service doing all the work it agreed to. Degraded says
        // that plainly, so an operator scaling up sees it and a supervisor does not restart it.
        return Task.FromResult(running >= capacity
            ? HealthCheckResult.Degraded($"At capacity: {running}/{capacity} run(s) in flight.")
            : HealthCheckResult.Healthy($"{running}/{capacity} run(s) in flight."));
    }
}
