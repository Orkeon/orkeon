using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Execution;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The runs of the bridge's callers (#445, ADR-019): one <see cref="AgentRunScope"/> per caller run id, so "one
/// browser per agent run" means on the bridge what it means on the caller. The caller ends a run with
/// <c>POST end-run</c>; a caller that gives no sign (a heartbeat or any request) for longer than the heartbeat window
/// loses every run it holds at once, without a grace delay. A caller that comes back after that gets new scopes.
/// </summary>
internal sealed partial class ToolBridgeRuns : IAsyncDisposable
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Caller> _callers = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private readonly ILogger _logger;
    private readonly ITimer _watch;

    /// <summary>Starts watching the callers' heartbeats.</summary>
    /// <param name="window">How long a caller may stay silent.</param>
    /// <param name="time">The clock the window is measured on.</param>
    /// <param name="logger">Logs a caller whose runs were ended.</param>
    public ToolBridgeRuns(TimeSpan window, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _window = window;
        _time = time;
        _logger = logger;
        var period = window / 4;
        _watch = time.CreateTimer(_ => EndSilentCallers(), null, period, period);
    }

    /// <summary>The number of runs the bridge holds for <paramref name="callerId"/>.</summary>
    /// <param name="callerId">The caller.</param>
    /// <returns>Zero for a caller the bridge does not know.</returns>
    public int RunsOf(string callerId)
    {
        lock (_sync)
            return _callers.TryGetValue(callerId, out var caller) ? caller.Runs.Count : 0;
    }

    /// <summary>
    /// The scope of the caller's run <paramref name="runId"/>, created at its first call; the caller is seen alive.
    /// </summary>
    /// <param name="callerId">The caller.</param>
    /// <param name="runId">The caller's run.</param>
    /// <returns>The scope the call runs in.</returns>
    public AgentRunScope ScopeOf(string callerId, string runId)
    {
        lock (_sync)
        {
            if (!_callers.TryGetValue(callerId, out var caller))
                _callers[callerId] = caller = new Caller();
            caller.LastSeen = _time.GetTimestamp();
            if (!caller.Runs.TryGetValue(runId, out var scope))
                caller.Runs[runId] = scope = AgentRunScope.Create(agentId: runId, agentRole: ToolBridgeProtocol.AuditCaller);
            return scope;
        }
    }

    /// <summary>Records a sign of life of <paramref name="callerId"/>; nothing for a caller without runs.</summary>
    /// <param name="callerId">The caller.</param>
    public void Touch(string callerId)
    {
        lock (_sync)
        {
            if (_callers.TryGetValue(callerId, out var caller))
                caller.LastSeen = _time.GetTimestamp();
        }
    }

    /// <summary>Ends the caller's run <paramref name="runId"/>: its scope releases what the tools put in it.</summary>
    /// <param name="callerId">The caller.</param>
    /// <param name="runId">The run.</param>
    /// <returns>A task that completes once the scope ended; at once for a run the bridge does not hold.</returns>
    public ValueTask EndAsync(string callerId, string runId)
    {
        AgentRunScope? scope = null;
        lock (_sync)
        {
            if (_callers.TryGetValue(callerId, out var caller))
            {
                caller.LastSeen = _time.GetTimestamp();
                if (caller.Runs.TryGetValue(runId, out var held))
                {
                    caller.Runs.Remove(runId);
                    scope = held;
                }

                if (caller.Runs.Count == 0)
                    _callers.Remove(callerId);
            }
        }

        return scope?.EndAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>Stops watching and ends every run held.</summary>
    /// <returns>A task that completes once every scope ended.</returns>
    public async ValueTask DisposeAsync()
    {
        await _watch.DisposeAsync().ConfigureAwait(false);
        List<AgentRunScope> scopes;
        lock (_sync)
        {
            scopes = [.. _callers.Values.SelectMany(c => c.Runs.Values)];
            _callers.Clear();
        }

        foreach (var scope in scopes)
            await scope.EndAsync().ConfigureAwait(false);
    }

    private void EndSilentCallers()
    {
        var now = _time.GetTimestamp();
        List<(string Caller, List<AgentRunScope> Scopes)> silent = [];
        lock (_sync)
        {
            foreach (var (id, caller) in _callers)
            {
                if (_time.GetElapsedTime(caller.LastSeen, now) > _window)
                    silent.Add((id, [.. caller.Runs.Values]));
            }

            foreach (var (id, _) in silent)
                _callers.Remove(id);
        }

        foreach (var (id, scopes) in silent)
        {
            LogCallerLost(_logger, id, scopes.Count, _window.TotalSeconds);
            foreach (var scope in scopes)
                _ = EndQuietlyAsync(scope);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Ending a scope never throws; this guards the timer's thread against a surprise all the same.")]
    private async System.Threading.Tasks.Task EndQuietlyAsync(AgentRunScope scope)
    {
        try
        {
            await scope.EndAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogEndFailed(_logger, ex.GetType().Name);
        }
    }

    private sealed class Caller
    {
        public long LastSeen { get; set; }

        public Dictionary<string, AgentRunScope> Runs { get; } = new(StringComparer.Ordinal);
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Tool bridge: caller {Caller} gave no sign for {Window} s; its {Runs} run(s) on the bridge are ended")]
    private static partial void LogCallerLost(ILogger logger, string caller, int runs, double window);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Tool bridge: ending a caller's run failed: {Error}")]
    private static partial void LogEndFailed(ILogger logger, string error);
}
