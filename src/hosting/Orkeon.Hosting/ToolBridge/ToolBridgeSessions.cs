using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Execution;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>What the bridge answers a call, a run or a session asked of <see cref="ToolBridgeSessions"/>.</summary>
internal enum SessionAdmission
{
    /// <summary>Admitted.</summary>
    Admitted,

    /// <summary>The session ended meanwhile: its heartbeat window passed, or the bridge stops.</summary>
    Lost,

    /// <summary>The session is at its limit (<c>MaxCallsPerSession</c>, <c>MaxRunsPerSession</c>).</summary>
    Full,

    /// <summary>A call with that id is already in flight in the session.</summary>
    Duplicate,
}

/// <summary>
/// The sessions of the bridge's callers (#445, ADR-019). A caller opens one with <c>POST session</c> after the token
/// check and gets a random id of 256 bits that only it holds; every later request names it. A session holds the
/// caller's runs, one <see cref="AgentRunScope"/> per caller run id, so "one browser per agent run" means on the
/// bridge what it means on the caller, and the calls in flight, by the caller's call id. Nothing of a session is
/// reachable from another: a run, a call or a cancellation is looked up in the session the request names.
/// <para>
/// A session that gives no sign (a heartbeat or any request) for longer than the heartbeat window ends at once,
/// without a grace delay: its calls in flight are cancelled and its runs ended. A request naming it is then answered
/// <see cref="ToolBridgeProtocol.SessionLostStatus"/>, and the caller opens another session for its next runs.
/// </para>
/// <para>
/// The bridge keeps the SHA-256 of each id, never the id itself, and names a session in its logs by a label of its
/// own (<c>session-3</c>).
/// </para>
/// </summary>
internal sealed partial class ToolBridgeSessions : IAsyncDisposable
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private readonly int _maxSessions;
    private readonly int _maxRuns;
    private readonly int _maxCalls;
    private readonly ILogger _logger;
    private readonly ITimer _watch;
    private long _opened;

    /// <summary>Starts watching the sessions' heartbeats.</summary>
    /// <param name="window">How long a session may stay silent.</param>
    /// <param name="maxSessions">The largest number of sessions held at once.</param>
    /// <param name="maxRuns">The largest number of runs one session holds.</param>
    /// <param name="maxCalls">The largest number of calls one session runs at once.</param>
    /// <param name="time">The clock the window is measured on.</param>
    /// <param name="logger">Logs a session that was ended for its silence.</param>
    public ToolBridgeSessions(TimeSpan window, int maxSessions, int maxRuns, int maxCalls, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _window = window;
        _maxSessions = maxSessions;
        _maxRuns = maxRuns;
        _maxCalls = maxCalls;
        _time = time;
        _logger = logger;
        var period = window / 4;
        _watch = time.CreateTimer(_ => EndSilentSessions(), null, period, period);
    }

    /// <summary>The number of sessions held.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
                return _sessions.Count;
        }
    }

    /// <summary>Opens a session.</summary>
    /// <returns>Its id, which only the caller holds; null when <c>MaxSessions</c> are held already.</returns>
    public string? Open()
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        lock (_sync)
        {
            if (_sessions.Count >= _maxSessions)
                return null;
            _sessions[KeyOf(id)] = new Session($"session-{++_opened}", _time.GetTimestamp());
        }

        return id;
    }

    /// <summary>The session <paramref name="id"/> names, seen alive; null for an id the bridge does not hold.</summary>
    /// <param name="id">The id the request carries.</param>
    /// <returns>The session, or null.</returns>
    public Session? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var key = KeyOf(id.Trim());
        lock (_sync)
        {
            if (!_sessions.TryGetValue(key, out var session))
                return null;
            session.LastSeen = _time.GetTimestamp();
            return session;
        }
    }

    /// <summary>The number of runs the session <paramref name="id"/> holds.</summary>
    /// <param name="id">The session id.</param>
    /// <returns>Zero for a session the bridge does not hold.</returns>
    public int RunsOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return 0;
        lock (_sync)
            return _sessions.TryGetValue(KeyOf(id), out var session) ? session.Runs.Count : 0;
    }

    /// <summary>The scope of the session's run <paramref name="runId"/>, created at its first call.</summary>
    /// <param name="session">The session.</param>
    /// <param name="runId">The caller's run.</param>
    /// <param name="scope">The scope the call runs in, when admitted.</param>
    /// <returns><see cref="SessionAdmission.Full"/> for a new run past <c>MaxRunsPerSession</c>.</returns>
    public SessionAdmission ScopeOf(Session session, string runId, out AgentRunScope? scope)
    {
        ArgumentNullException.ThrowIfNull(session);
        scope = null;
        lock (_sync)
        {
            if (session.Ended)
                return SessionAdmission.Lost;
            session.LastSeen = _time.GetTimestamp();
            if (session.Runs.TryGetValue(runId, out var held))
            {
                scope = held;
                return SessionAdmission.Admitted;
            }

            if (session.Runs.Count >= _maxRuns)
                return SessionAdmission.Full;
            session.Runs[runId] = scope = AgentRunScope.Create(agentId: runId, agentRole: ToolBridgeProtocol.AuditCaller);
            return SessionAdmission.Admitted;
        }
    }

    /// <summary>Records the call <paramref name="callId"/> in flight in the session, which <see cref="Cancel"/> reaches.</summary>
    /// <param name="session">The session.</param>
    /// <param name="callId">The caller's id of the call.</param>
    /// <param name="cancellation">Cancels the call.</param>
    /// <returns><see cref="SessionAdmission.Full"/> past <c>MaxCallsPerSession</c>, <see cref="SessionAdmission.Duplicate"/> for an id in flight.</returns>
    public SessionAdmission BeginCall(Session session, string callId, CancellationTokenSource cancellation)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_sync)
        {
            if (session.Ended)
                return SessionAdmission.Lost;
            if (session.Calls.ContainsKey(callId))
                return SessionAdmission.Duplicate;
            if (session.Calls.Count >= _maxCalls)
                return SessionAdmission.Full;
            session.Calls[callId] = cancellation;
            return SessionAdmission.Admitted;
        }
    }

    /// <summary>Forgets the call <paramref name="callId"/> of the session, once answered; the session is seen alive.</summary>
    /// <param name="session">The session.</param>
    /// <param name="callId">The caller's id of the call.</param>
    public void EndCall(Session session, string callId)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_sync)
        {
            session.Calls.Remove(callId);
            session.LastSeen = _time.GetTimestamp();
        }
    }

    /// <summary>Cancels the call <paramref name="callId"/> of the session; nothing for a call it does not run.</summary>
    /// <param name="session">The session.</param>
    /// <param name="callId">The caller's id of the call.</param>
    public void Cancel(Session session, string callId)
    {
        ArgumentNullException.ThrowIfNull(session);
        CancellationTokenSource? call;
        lock (_sync)
            session.Calls.TryGetValue(callId, out call);
        CancelQuietly(call);
    }

    /// <summary>Ends the session's run <paramref name="runId"/>: its scope releases what the tools put in it.</summary>
    /// <param name="session">The session.</param>
    /// <param name="runId">The run.</param>
    /// <returns>A task that completes once the scope ended; at once for a run the session does not hold.</returns>
    [SuppressMessage("Reliability", "CA2000", Justification = "The scope removed is ended by EndAsync, which releases what it holds.")]
    public ValueTask EndRunAsync(Session session, string runId)
    {
        ArgumentNullException.ThrowIfNull(session);
        AgentRunScope? scope = null;
        lock (_sync)
        {
            if (session.Runs.Remove(runId, out var held))
                scope = held;
        }

        return scope?.EndAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>Stops watching, cancels every call in flight and ends every run held.</summary>
    /// <returns>A task that completes once every scope ended.</returns>
    public async ValueTask DisposeAsync()
    {
        await _watch.DisposeAsync().ConfigureAwait(false);
        List<Session> sessions;
        lock (_sync)
        {
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            foreach (var scope in Close(session))
                await EndQuietlyAsync(scope).ConfigureAwait(false);
        }
    }

    private static string KeyOf(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    // Marks the session ended, cancels its calls and hands back its scopes to end.
    private List<AgentRunScope> Close(Session session)
    {
        List<AgentRunScope> scopes;
        List<CancellationTokenSource> calls;
        lock (_sync)
        {
            session.Ended = true;
            scopes = [.. session.Runs.Values];
            calls = [.. session.Calls.Values];
            session.Runs.Clear();
        }

        foreach (var call in calls)
            CancelQuietly(call);
        return scopes;
    }

    private static void CancelQuietly(CancellationTokenSource? call)
    {
        try
        {
            call?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The call ended meanwhile: nothing is left to cancel.
        }
    }

    private void EndSilentSessions()
    {
        var now = _time.GetTimestamp();
        List<(string Key, Session Session)> silent = [];
        lock (_sync)
        {
            foreach (var (key, session) in _sessions)
            {
                if (_time.GetElapsedTime(session.LastSeen, now) > _window)
                    silent.Add((key, session));
            }

            foreach (var (key, _) in silent)
                _sessions.Remove(key);
        }

        foreach (var (_, session) in silent)
        {
            var scopes = Close(session);
            LogSessionLost(_logger, session.Label, scopes.Count, _window.TotalSeconds);
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

    /// <summary>One caller's session: its runs and its calls in flight, under the lock of its <see cref="ToolBridgeSessions"/>.</summary>
    internal sealed class Session
    {
        /// <summary>Builds the session.</summary>
        /// <param name="label">Its name in the logs.</param>
        /// <param name="lastSeen">The timestamp of its opening.</param>
        public Session(string label, long lastSeen)
        {
            Label = label;
            LastSeen = lastSeen;
        }

        /// <summary>Its name in the logs; never its id.</summary>
        public string Label { get; }

        /// <summary>The timestamp of its last sign.</summary>
        public long LastSeen { get; set; }

        /// <summary>Whether it ended.</summary>
        public bool Ended { get; set; }

        /// <summary>Its runs, by the caller's run id.</summary>
        public Dictionary<string, AgentRunScope> Runs { get; } = new(StringComparer.Ordinal);

        /// <summary>Its calls in flight, by the caller's call id.</summary>
        public Dictionary<string, CancellationTokenSource> Calls { get; } = new(StringComparer.Ordinal);
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Tool bridge: {Session} gave no sign for {Window} s; it is ended with its {Runs} run(s) on the bridge")]
    private static partial void LogSessionLost(ILogger logger, string session, int runs, double window);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Tool bridge: ending a caller's run failed: {Error}")]
    private static partial void LogEndFailed(ILogger logger, string error);
}
