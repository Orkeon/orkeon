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
    /// <summary>Runs <paramref name="crewName"/> and reports as it goes.</summary>
    Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Action<string>? onStarted = null,
        CancellationToken cancellationToken = default);
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

    /// <summary>Builds the runner over the host's services and its registry.</summary>
    public CrewRunner(
        IServiceScopeFactory scopes,
        CrewHostRegistry registry,
        IHost host,
        IOptions<OrkeonHostOptions> options,
        ILogger<CrewRunner> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// <param name="cancellationToken">Stops the run.</param>
    public async Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Action<string>? onStarted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewName);

        var hosted = _registry.Find(crewName);
        if (hosted is null)
            return new HostedRunResult(HostedRunOutcome.UnknownCrew, null, $"No hosted crew named '{crewName}'.");

        var run = _registry.TryStart(crewName, origin, cancellationToken);
        if (run is null)
        {
            return new HostedRunResult(
                HostedRunOutcome.Busy,
                null,
                $"'{hosted.Name}' is already running {hosted.Profile.MaxConcurrentRuns} conversation(s); try again shortly.");
        }

        LogRunStarted(run.Id, hosted.Name, origin);
        onStarted?.Invoke(run.Id);

        try
        {
            run.Cancellation.CancelAfter(_options.RunTimeout);

            // One scope per run. Anything registered scoped — the memory scope above all —
            // belongs to this run and dies with it.
            using var scope = _scopes.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();

            var crew = await RunnerExecution
                .LoadCrewAsync(_host, factory, hosted.Path, _logger, run.Cancellation.Token)
                .ConfigureAwait(false);

            onProgress?.Invoke($"Running '{hosted.Name}'…");

            var output = await orchestrator
                .KickoffAsync(crew.Id, Application.Interfaces.Services.CrewInput.Empty(prompt), run.Cancellation.Token)
                .ConfigureAwait(false);

            LogRunFinished(run.Id, hosted.Name);
            return new HostedRunResult(
                HostedRunOutcome.Completed,
                run.Id,
                output.FinalOutput ?? string.Empty);
        }
        catch (OperationCanceledException)
        {
            LogRunCancelled(run.Id, hosted.Name);
            return new HostedRunResult(HostedRunOutcome.Cancelled, run.Id, "The run was stopped.");
        }
#pragma warning disable CA1031 // A hosted run must not take the daemon down with it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRunFailed(ex, run.Id, hosted.Name);
            return new HostedRunResult(HostedRunOutcome.Failed, run.Id, ex.Message);
        }
        finally
        {
            _registry.Finish(run.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started: crew '{CrewName}' from {Origin}")]
    private partial void LogRunStarted(string runId, string crewName, string origin);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} of '{CrewName}' finished")]
    private partial void LogRunFinished(string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} of '{CrewName}' was stopped before finishing")]
    private partial void LogRunCancelled(string runId, string crewName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run {RunId} of '{CrewName}' failed")]
    private partial void LogRunFailed(Exception ex, string runId, string crewName);
}
