using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;

namespace Orkeon.Host;

/// <summary>
/// The run history of one hosted run (FWK-08): when <c>Orkeon:RunHistory</c> is on (it is, by default, in the daemon),
/// the run gets its own <see cref="RunEventPublisher"/> over the history, opens the run's identity
/// (<see cref="IRunScope"/>, the id the checkpoint session carries) before <c>run.started</c>, and the strategies'
/// task events reach it through <see cref="RunEventObserver"/>. One instance per run scope.
/// <para>
/// The identity is the run's <see cref="ExecutionId"/>, and it is the daemon's own run id too (FWK-14): the registry mints
/// the id as an <see cref="ExecutionId"/> and <see cref="Begin"/> adopts it, so the history, the checkpoints, the A2A task
/// and the host API name a run by one id. <c>hostRunId</c> in <c>run.started</c> repeats it.
/// </para>
/// </summary>
internal sealed class HostRunRecording
{
    /// <summary>The exit code <c>orkeon run</c> gives a cancelled run, which the stream's <c>run.finished</c> mirrors.</summary>
    private const int CancelledExitCode = 130;

    private readonly ConfiguredRunHistory? _history;
    private readonly IRunScope _runScope;
    private readonly TimeProvider _time;
    private readonly ILogger<HostRunRecording> _logger;
    private RunEventPublisher? _publisher;
    private RunEventObserver? _observer;

    /// <summary>Resolved per run scope; the history is null when it is not enabled.</summary>
    public HostRunRecording(IRunScope runScope, ILogger<HostRunRecording> logger, ConfiguredRunHistory? history = null, TimeProvider? time = null)
    {
        _runScope = runScope ?? throw new ArgumentNullException(nameof(runScope));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _history = history;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True once <see cref="Begin"/> opened a recording.</summary>
    public bool IsActive => _publisher is not null;

    /// <summary>Records that the run's crew completed (or not); read when the recording ends.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Records that the run stopped on a question to a human (FWK-23): its end is <c>waiting_for_input</c>, exit 75.</summary>
    public bool WaitingForInput { get; set; }

    /// <summary>
    /// The task-event observer in front of <paramref name="inner"/> when the run is recorded, else <paramref name="inner"/>
    /// itself: the run's own hook keeps receiving every callback either way.
    /// </summary>
    public ICrewExecutionHook Hook(ICrewExecutionHook inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return _publisher is null ? inner : _observer ??= new RunEventObserver(_publisher, inner, stream: false);
    }

    /// <summary>
    /// Opens the recording of a run: the run's identity, then <c>run.started</c>. Disposing the result writes
    /// <c>run.finished</c> and closes the identity. Without a history only the identity is opened.
    /// </summary>
    /// <param name="crewName">The hosted crew.</param>
    /// <param name="hostRunId">The daemon's own id of the run, which is the run's <see cref="ExecutionId"/>.</param>
    /// <param name="idempotency">The idempotency key and request hash of an API run, recorded in <c>run.started</c> so a restart finds them.</param>
    /// <param name="cancelled">Whether the run was stopped or timed out, asked at the end.</param>
    public IDisposable Begin(string crewName, string hostRunId, Func<bool> cancelled, RunIdempotency? idempotency = null)
    {
        // The identity is opened whether or not the history is on (FWK final review H2): the checkpoints, the broker and
        // the events name the run by the id the host API returned, never by one the orchestrator would mint.
        var executionId = ExecutionId.From(hostRunId);
        var identity = _runScope.Enter(new RunScope(executionId, crewName));
        if (_history is null)
            return identity;

        _publisher = new RunEventPublisher([_history.Sink], _time, _runScope, executionId.ToString(), _logger);
        _publisher.Emit(RunEventKinds.RunStarted, new
        {
            target = crewName,
            crewKey = crewName,
            hostRunId,
            stream = false,
            idempotencyKey = idempotency?.Key,
            requestHash = idempotency?.Hash,
        });
        return new Recording(this, identity, cancelled, Stopwatch.GetTimestamp());
    }

    private void Finish(Func<bool> cancelled, long started)
    {
        var success = Succeeded;
        var exitCode = WaitingForInput ? Orkeon.Constants.Cli.RunExitCodes.WaitingForInput : success ? 0 : cancelled() ? CancelledExitCode : 1;
        _publisher?.Emit(RunEventKinds.RunFinished, new
        {
            success,
            exitCode,
            status = WaitingForInput ? RunFinishedStatuses.WaitingForInput : null,
            durationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        });
    }

    private sealed class Recording(HostRunRecording owner, IDisposable identity, Func<bool> cancelled, long started) : IDisposable
    {
        public void Dispose()
        {
            owner.Finish(cancelled, started);
            identity.Dispose();
        }
    }
}
