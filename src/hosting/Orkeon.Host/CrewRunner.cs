using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Hosting;

namespace Orkeon.Host;

/// <summary>Why a run did not happen, or how it ended.</summary>
internal enum HostedRunOutcome
{
    /// <summary>The crew ran to completion.</summary>
    Completed,

    /// <summary>The configuration declares no crew by that name.</summary>
    UnknownCrew,

    /// <summary>The crew is already at its concurrency limit.</summary>
    Busy,

    /// <summary>Someone asked it to stop, or the host is shutting down.</summary>
    Cancelled,

    /// <summary>It failed.</summary>
    Failed,
}

/// <summary>What a hosted run produced.</summary>
internal sealed record HostedRunResult(HostedRunOutcome Outcome, string? RunId, string Message)
{
    /// <summary>Whether the crew actually ran and finished.</summary>
    public bool Succeeded => Outcome == HostedRunOutcome.Completed;
}

/// <summary>
/// Runs a hosted crew. A port so the gateway can be exercised without a host, a model or a
/// crew — the routing, the authorization and the reply order are worth testing on their own.
/// </summary>
internal interface ICrewRunner
{
    /// <summary>
    /// Runs <paramref name="crewName"/> and reports as it goes. Deliberately takes no
    /// cancellation token: a hosted run is stopped through the registry (a Stop press, the
    /// drain, its own deadline), never by a caller's ambient token — linking the two is how
    /// SIGTERM used to kill every run at t=0 while the grace period waited for corpses.
    /// </summary>
    Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Func<string, Task>? onStarted = null);
}

/// <summary>
/// Runs one hosted crew, in its own dependency-injection scope (GATE-02).
/// <para>
/// **The scope is the isolation.** <c>IMemoryScope</c> is registered scoped, so two runs
/// sharing a provider would share a memory — the leak the gateway specification calls the most
/// serious risk of this design. Creating a scope per run is what keeps one conversation from
/// reading another's, and a test holds it rather than the comment alone.
/// </para>
/// <para>
/// A run also carries its own deadline. A daemon has nobody watching to press Ctrl-C, so a run
/// with no timeout is a stuck daemon waiting for a model that will never answer.
/// </para>
/// </summary>
internal sealed partial class CrewRunner : ICrewRunner
{
    private readonly IServiceScopeFactory _scopes;
    private readonly CrewHostRegistry _registry;
    private readonly IHost _host;
    private readonly OrkeonHostOptions _options;
    private readonly ILogger<CrewRunner> _logger;

    /// <summary>
    /// Maps a hosted crew's name to the virtual path its directory was mounted under.
    /// Optional so a test can compose a runner without the boot-time plan; absent, the
    /// configured path is used verbatim, which is what a caller mounting its own crews wants.
    /// </summary>
    private readonly HostCrewMountPlan? _mountPlan;

    /// <summary>Builds the runner over the host's services and its registry.</summary>
    public CrewRunner(
        IServiceScopeFactory scopes,
        CrewHostRegistry registry,
        IHost host,
        IOptions<OrkeonHostOptions> options,
        ILogger<CrewRunner> logger,
        HostCrewMountPlan? mountPlan = null)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mountPlan = mountPlan;
    }

    /// <summary>
    /// Runs <paramref name="crewName"/> against <paramref name="prompt"/>, reporting through
    /// <paramref name="onProgress"/> as tasks finish. Refusals come back as results, never as
    /// exceptions: "unknown crew" and "busy" are answers a channel relays to a person.
    /// </summary>
    /// <param name="crewName">Which hosted crew to run.</param>
    /// <param name="prompt">What to run it on.</param>
    /// <param name="origin">Where the request came from, for the registry and the logs.</param>
    /// <param name="onProgress">Receives progress lines as the run advances.</param>
    /// <param name="onStarted">
    /// Receives the run identifier the moment the slot is reserved — before the crew loads, let
    /// alone finishes. A caller that only learned the id at the end could never stop the run it
    /// started, which is what <c>/stop</c> is for.
    /// </param>
    public async Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Func<string, Task>? onStarted = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewName);

        var hosted = _registry.Find(crewName);
        if (hosted is null)
            return new HostedRunResult(HostedRunOutcome.UnknownCrew, null, $"No hosted crew named '{crewName}'.");

        var run = _registry.TryStart(crewName, origin);
        if (run is null)
        {
            return new HostedRunResult(
                HostedRunOutcome.Busy,
                null,
                $"'{hosted.Name}' is already running {hosted.Profile.MaxConcurrentRuns} conversation(s); try again shortly.");
        }

        LogRunStarted(run.Id, hosted.Name, origin);

        Orkeon.Domain.Common.CrewId? crewId = null;
        try
        {
            // The deadline is armed BEFORE the acknowledgement: the ack is a network round
            // trip, and a rate-limited channel could otherwise hold an admitted slot with no
            // timeout at all.
            run.Cancellation.CancelAfter(_options.RunTimeout);

            // Inside the try: a throwing callback must release the slot like any other
            // failure, or the crew stays one seat closer to permanently "busy".
            if (onStarted is not null)
                await onStarted(run.Id).ConfigureAwait(false);

            // One scope per run. Anything registered scoped belongs to this run and dies with
            // it — the crew repository above all, which is what keeps one conversation's crew
            // out of another's resolution. Per-crew memory is released in the finally: a
            // hosted run's memory cannot outlive it by construction (every message loads a
            // fresh crew with a fresh id).
            using var scope = _scopes.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();

            // Task completions flow to the conversation as they happen — the throttled
            // responder downstream spaces them out. Resolved from the run's own scope, so
            // two concurrent runs never share a counter or a callback.
            if (onProgress is not null && scope.ServiceProvider.GetService<RunProgressHook>() is { } progressHook)
                progressHook.OnProgress = onProgress;

            var crewPath =
                _mountPlan?.VirtualPaths.TryGetValue(hosted.Name, out var virtualPath) == true
                    ? virtualPath
                    : hosted.Path;
            var crew = await RunnerExecution
                .LoadCrewAsync(_host, factory, crewPath, _logger, run.Cancellation.Token)
                .ConfigureAwait(false);
            crewId = crew.Id;

            onProgress?.Invoke($"Running '{hosted.Name}'…");

            var output = await orchestrator
                .KickoffAsync(crew.Id, Application.Interfaces.Services.CrewInput.Empty(prompt), run.Cancellation.Token)
                .ConfigureAwait(false);

            // KickoffAsync never throws — its fault barrier converts everything into an
            // output. Reading that output as Completed reported every timeout, /stop and
            // crew failure as a success, logged "finished", and handed the user the
            // apology string as if it were the answer. Order matters here: a completed
            // answer beats a stop that raced in after the fact — the user already paid
            // for it, and "The run was stopped." would throw it away.
            if (output.Succeeded)
            {
                LogRunFinished(run.Id, hosted.Name);
                return new HostedRunResult(
                    HostedRunOutcome.Completed,
                    run.Id,
                    output.FinalOutput ?? string.Empty);
            }

            if (run.Cancellation.IsCancellationRequested)
                return Cancelled(run, hosted.Name);

            LogRunFailedWithOutput(run.Id, hosted.Name, output.FinalOutput ?? string.Empty);
            return new HostedRunResult(HostedRunOutcome.Failed, run.Id, FailureMessage(run.Id));
        }
        catch (OperationCanceledException)
        {
            return Cancelled(run, hosted.Name);
        }
#pragma warning disable CA1031 // A hosted run must not take the daemon down with it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The detail goes to the log, not to the chat: a load failure carries absolute
            // server paths and a provider failure carries endpoints, and the thread's
            // membership is not the operator set.
            LogRunFailed(ex, run.Id, hosted.Name);
            return new HostedRunResult(HostedRunOutcome.Failed, run.Id, FailureMessage(run.Id));
        }
        finally
        {
            _registry.Finish(run.Id);
            ReleasePerRunState(crewId);
        }
    }

    private static string FailureMessage(string runId) =>
        $"The run failed (run {runId}). Details are in the host log.";

    /// <summary>
    /// One cancelled token, two different stories: a person pressed Stop, or the run hit its
    /// own deadline. The registry recorded which, and the user-facing sentence and the log
    /// line both say the right one.
    /// </summary>
    private HostedRunResult Cancelled(HostedRun run, string crewName)
    {
        if (run.StopRequested)
        {
            LogRunCancelled(run.Id, crewName);
            return new HostedRunResult(HostedRunOutcome.Cancelled, run.Id, "The run was stopped.");
        }

        LogRunTimedOut(run.Id, crewName, _options.RunTimeout);
        return new HostedRunResult(
            HostedRunOutcome.Cancelled, run.Id,
            $"The run timed out after {_options.RunTimeout}.");
    }

    /// <summary>
    /// Forgets what the process-wide services accumulated for this run's crew. Every hosted
    /// message loads a fresh crew with a fresh id, and the memory service and the provider
    /// registry are singletons keyed by that id: without this, a daemon leaks one entry per
    /// conversation, forever.
    /// </summary>
    private void ReleasePerRunState(Orkeon.Domain.Common.CrewId? crewId)
    {
        if (crewId is null)
            return;

        _host.Services.GetService<Application.Interfaces.Services.IMemoryService>()?.ReleaseMemorySystem(crewId);
        _host.Services.GetService<Application.Memory.CrewMemoryProviderRegistry>()?.Remove(crewId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started: crew '{CrewName}' from {Origin}")]
    private partial void LogRunStarted(string runId, string crewName, string origin);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} of '{CrewName}' finished")]
    private partial void LogRunFinished(string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} of '{CrewName}' was stopped before finishing")]
    private partial void LogRunCancelled(string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} of '{CrewName}' timed out after {Timeout}")]
    private partial void LogRunTimedOut(string runId, string crewName, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} of '{CrewName}' failed")]
    private partial void LogRunFailed(Exception ex, string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} of '{CrewName}' failed: {Output}")]
    private partial void LogRunFailedWithOutput(string runId, string crewName, string output);
}
