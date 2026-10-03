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
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: validates operator-supplied crew paths at service start, before any VFS mount exists.")]
internal sealed partial class CrewHostService : BackgroundService
{
    private readonly CrewHostRegistry _registry;
    private readonly OrkeonHostOptions _options;
    private readonly ILogger<CrewHostService> _logger;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<string> _definedLlmProfiles;

    /// <summary>Builds the service over the registry and the host's options.</summary>
    /// <param name="registry">The hosted crews.</param>
    /// <param name="options">The service's configuration.</param>
    /// <param name="logger">The service's logger.</param>
    /// <param name="timeProvider">The clock the drain reads; the system's by default.</param>
    /// <param name="configuration">
    /// The host configuration, for the LLM profiles it defines (<c>Llm:Profiles</c>), which the
    /// <see cref="OrkeonHostOptions.LlmProfiles"/> allow-list must name; null defines none.
    /// </param>
    public CrewHostService(
        CrewHostRegistry registry,
        IOptions<OrkeonHostOptions> options,
        ILogger<CrewHostService> logger,
        TimeProvider? timeProvider = null,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
        _definedLlmProfiles = configuration is null
            ? []
            : Orkeon.Infrastructure.LLMs.Profiles.LlmSettings.ProfileNames(configuration);
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Validated in StartAsync, deliberately: a refused configuration fails the host
        // start itself — BEFORE UseSystemd() sends READY=1. The first version validated
        // after readiness, so systemd recorded a host with no crew, or with a crew path
        // that does not exist, as "active (running)" right up to its clean exit.
        ValidateConfiguration();
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // The drain lives HERE, not after the stopping token fires in ExecuteAsync. On
        // .NET 10 the base's StopAsync does not wait for ExecuteAsync's post-cancellation
        // tail — a probe showed drain-after-token skipped in 299 runs out of 300 — so a
        // drain written there looked graceful and essentially never ran before the host
        // moved on to stopping everything else. StopAsync is awaited by the host, within
        // the ShutdownTimeout this service's options budget.
        //
        // Admission closes first (GAP-35): this service stops before the chat channel and the
        // A2A server — they deliver the answers of the runs in flight —, so a message or a task
        // arriving during the grace is answered "the host is stopping" instead of starting a run
        // the drain would wait for, then stop.
        _registry.CloseAdmission();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await DrainAsync().ConfigureAwait(false);
    }

    private void ValidateConfiguration()
    {
        if (_registry.Crews.Count == 0)
        {
            // A daemon hosting nothing is a configuration mistake, not a state to sit in
            // quietly: systemd would report it as healthy forever.
            LogNoCrewsConfigured(OrkeonHostOptions.SectionName);
            throw new HostConfigurationException(
                $"No hosted crew is configured under '{OrkeonHostOptions.SectionName}:Crews'.");
        }

        foreach (var crew in _registry.Crews)
        {
            if (string.IsNullOrWhiteSpace(crew.Name) || string.IsNullOrWhiteSpace(crew.Path))
                throw new HostConfigurationException("Every hosted crew needs a Name and a Path.");

            // OUT-OF-SCOPE: probing the operator-supplied crew path; host bootstrap runs
            // before the VFS mounts exist. Discovered at startup on purpose — the first
            // version only found a missing crew on the first user message, when the daemon
            // was already "ready" and every run could only fail.
            if (!File.Exists(crew.Path) && !Directory.Exists(crew.Path))
                throw new HostConfigurationException(
                    $"Hosted crew '{crew.Name}' points at '{crew.Path}', which does not exist.");

            if (crew.Profile.MaxConcurrentRuns < 1)
                throw new HostConfigurationException(
                    $"Hosted crew '{crew.Name}' declares MaxConcurrentRuns {crew.Profile.MaxConcurrentRuns}; at least 1 is required.");
        }

        // A chat route names a crew (GAP-11), and the registry finds crews case-insensitively:
        // two crews answering to one name would leave every message to the first of them.
        var duplicate = _registry.Crews
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new HostConfigurationException(
                $"Two hosted crews are named {string.Join(" and ", duplicate.Select(c => $"'{c.Name}'"))}; "
                + "crew names must be unique (they compare case-insensitively).");

        // Zero cancels every run at its first instant; past the CancelAfter ceiling the
        // runner would throw on every start. Both are configuration mistakes, refused here
        // with the words to fix them rather than discovered one failed run at a time.
        if (_options.RunTimeout <= TimeSpan.Zero || _options.RunTimeout.TotalMilliseconds > int.MaxValue)
            throw new HostConfigurationException(
                $"RunTimeout must be positive and under ~24.8 days; got {_options.RunTimeout}.");

        if (_options.ShutdownGracePeriod < TimeSpan.Zero)
            throw new HostConfigurationException(
                $"ShutdownGracePeriod cannot be negative; got {_options.ShutdownGracePeriod}.");

        // The allow-list names profiles the configuration defines (GAP-17): a typo there would
        // otherwise refuse, one crew load at a time, a profile the operator meant to offer.
        var undefined = (_options.LlmProfiles ?? [])
            .Where(name => !Orkeon.Application.Interfaces.Ports.LlmProfiles.IsDefault(name)
                && !_definedLlmProfiles.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (undefined.Count > 0)
            throw new HostConfigurationException(
                $"{OrkeonHostOptions.SectionName}:LlmProfiles names {string.Join(", ", undefined.Select(n => $"'{n}'"))}, "
                + $"which Llm:Profiles does not define. Defined profiles: "
                + $"{string.Join(", ", new[] { Orkeon.Application.Interfaces.Ports.LlmProfiles.Default }.Concat(_definedLlmProfiles))}.");
    }

    /// <summary>
    /// Gives runs in flight their grace period, then asks them to stop. Killing them the
    /// instant a signal arrives would lose work a few seconds from being finished; waiting
    /// forever would earn a SIGKILL, which loses it anyway and less politely.
    /// </summary>
    private async Task DrainAsync()
    {
        var deadline = _time.GetUtcNow() + _options.ShutdownGracePeriod;
        var inFlight = _registry.Running.Count;
        if (inFlight == 0)
            return;

        LogDraining(inFlight, _options.ShutdownGracePeriod);

        while (_registry.Running.Count > 0 && _time.GetUtcNow() < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);

        var stopped = _registry.RequestStopAll();
        if (stopped > 0)
            LogStoppedRemaining(stopped);

        // Give the cancelled runs a moment to actually unwind and release their slots —
        // asking and immediately leaving would hand systemd a process still mid-teardown.
        var teardown = _time.GetUtcNow() + TimeSpan.FromSeconds(5);
        while (_registry.Running.Count > 0 && _time.GetUtcNow() < teardown)
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);
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
