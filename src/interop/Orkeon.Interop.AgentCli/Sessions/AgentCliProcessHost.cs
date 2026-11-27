using Microsoft.Extensions.Logging;
using Orkeon.Interop.AgentCli.Engine;
using Orkeon.Interop.AgentCli.Process;

namespace Orkeon.Interop.AgentCli.Sessions;

/// <summary>
/// Every agent CLI process the host runs, whatever the run (plan 4.5). Opening one beyond
/// <see cref="AgentCliOptions.MaxProcesses"/> ends the session idle the longest; when every session is in a
/// call, the new one opens anyway and a warning says so — a call is never refused for it. Disposing the host
/// ends every process.
/// </summary>
internal sealed partial class AgentCliProcessHost : IAsyncDisposable
{
    private readonly IAgentCliLauncher _launcher;
    private readonly IAgentCliEngine _engine;
    private readonly AgentCliOptions _options;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly List<AgentCliSession> _sessions = [];

    private readonly TimeProvider _time;

    public AgentCliProcessHost(IAgentCliLauncher launcher, IAgentCliEngine engine, AgentCliOptions options, ILogger logger, TimeProvider? time = null)
    {
        _launcher = launcher;
        _engine = engine;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public AgentCliOptions Options => _options;

    /// <summary>The sessions whose process runs.</summary>
    public int LiveSessions
    {
        get
        {
            lock (_gate)
                return _sessions.Count(session => !session.IsClosed);
        }
    }

    /// <summary>Starts a process for a new session, already acquired by its caller.</summary>
    public async Task<AgentCliSession> OpenAsync(string fingerprint, string systemPrompt, string? model, string? effort)
    {
        AgentCliSession? evicted = null;
        var crowded = false;
        lock (_gate)
        {
            _sessions.RemoveAll(session => session.IsClosed);
            if (_sessions.Count >= Math.Max(1, _options.MaxProcesses))
            {
                // Taken here, so that no call can pick the session up between this choice and its end.
                evicted = _sessions.Where(session => !session.IsBusy).OrderBy(session => session.LastUsed)
                    .FirstOrDefault(session => session.TryAcquire());
                if (evicted is null)
                    crowded = true;
                else
                    _sessions.Remove(evicted);
            }
        }

        if (evicted is not null)
            await evicted.DisposeAsync().ConfigureAwait(false);
        else if (crowded)
            LogCrowded(_options.MaxProcesses);

        var process = _launcher.Start(_engine, systemPrompt, model, effort);
        var session = new AgentCliSession(process, _engine, _options, fingerprint, _logger, _time);
        session.TryAcquire();
        lock (_gate)
            _sessions.Add(session);
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        List<AgentCliSession> sessions;
        lock (_gate)
        {
            sessions = [.. _sessions];
            _sessions.Clear();
        }

        foreach (var session in sessions)
            await session.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 40, Level = LogLevel.Warning,
        Message = "Every one of the {MaxProcesses} agent CLI processes is in a call: one more starts. Raise Llm:AgentCli:MaxProcesses, or run fewer agents at once.")]
    private partial void LogCrowded(int maxProcesses);
}

/// <summary>
/// The sessions of one run (<see cref="Orkeon.Domain.Execution.AgentRunScope"/>): a call continues the session
/// whose conversation it extends, else opens one (plan 4.4). Ending the run ends its processes.
/// </summary>
internal sealed class AgentCliSessionStore : IAsyncDisposable
{
    private readonly AgentCliProcessHost _host;
    private readonly Lock _gate = new();
    private readonly List<AgentCliSession> _sessions = [];

    public AgentCliSessionStore(AgentCliProcessHost host) => _host = host;

    /// <summary>The sessions of the run still open, for tests and diagnostics.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _sessions.Count(session => !session.IsClosed);
        }
    }

    /// <summary>
    /// The session that continues <paramref name="keys"/>, acquired; else a new one. A session of the same
    /// fingerprint that can no longer be continued is closed.
    /// </summary>
    public async Task<AgentCliSession> AcquireAsync(string fingerprint, IReadOnlyList<AgentCliMessageKey> keys, string systemPrompt, string? model, string? effort)
    {
        var stale = new List<AgentCliSession>();
        lock (_gate)
        {
            _sessions.RemoveAll(session => session.IsClosed);
            foreach (var session in _sessions)
            {
                // Acquired first: the history of a session in a call is being written by that call.
                if (session.Fingerprint != fingerprint || !session.TryAcquire())
                    continue;
                if (session.Continues(keys))
                    return session;
                session.Abandon();
            }

            foreach (var session in _sessions.Where(session => session.Fingerprint == fingerprint && session.Diverged && !session.IsBusy).ToList())
            {
                _sessions.Remove(session);
                stale.Add(session);
            }
        }

        foreach (var session in stale)
            await session.DisposeAsync().ConfigureAwait(false);

        var opened = await _host.OpenAsync(fingerprint, systemPrompt, model, effort).ConfigureAwait(false);
        lock (_gate)
            _sessions.Add(opened);
        return opened;
    }

    public async ValueTask DisposeAsync()
    {
        List<AgentCliSession> sessions;
        lock (_gate)
        {
            sessions = [.. _sessions];
            _sessions.Clear();
        }

        foreach (var session in sessions)
            await session.DisposeAsync().ConfigureAwait(false);
    }
}
