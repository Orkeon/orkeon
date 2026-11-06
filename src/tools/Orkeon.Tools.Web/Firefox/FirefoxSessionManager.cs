using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// Gives each agent run its browser session. The session lives in the run's <see cref="AgentRunScope"/>:
/// Firefox starts at the run's first action, the next ones find the page where the last left it, and
/// the session closes with the run. Agents running side by side have their own scopes, hence their own
/// sessions: a browser each by default, tabs of their own in one shared browser per profile with
/// <c>SharedInstance</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Outside any run, an action gets a session of its own, closed right after it.</item>
/// <item>A run's browser starts on the profile its first action names, or the host's default; a later
/// action that names another profile is refused until <c>close</c>.</item>
/// <item>An action that fails for a reason the agent can act on (<see cref="IExpectedToolError"/>) keeps
/// the browser; any other failure, a cancellation, or a browser that has gone closes it, and the next
/// action starts a new one.</item>
/// </list>
/// </remarks>
internal sealed class FirefoxSessionManager : IDisposable
{
    private static readonly SlotKey s_key = new();

    private readonly IFirefoxLauncher _launcher;
    private readonly FirefoxToolOptions _options;
    private readonly IFileSystemService _fileSystem;
    private readonly FirefoxSessionTimings? _timings;
    private readonly ILogger? _logger;
    private readonly FirefoxBrowserPool? _pool;

    public FirefoxSessionManager(
        IFirefoxLauncher launcher, FirefoxToolOptions options, IFileSystemService fileSystem,
        ILogger? logger = null, FirefoxSessionTimings? timings = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _launcher = launcher;
        _options = options;
        _fileSystem = fileSystem;
        _logger = logger;
        _timings = timings;
        if (options.SharedInstance)
            _pool = new FirefoxBrowserPool(launcher, options, fileSystem, logger, timings);
    }

    /// <summary>The shared browsers now running; <c>null</c> when each run has its own.</summary>
    public FirefoxBrowserPool? Pool => _pool;

    /// <summary>
    /// Runs <paramref name="operation"/> on the current run's session, started first when needed on
    /// <paramref name="profile"/>. When <paramref name="named"/>, the agent asked for that profile: a
    /// session already open on another one refuses.
    /// </summary>
    /// <exception cref="FirefoxException">Firefox is not installed, did not start, or the profile is missing, held, or not the run's.</exception>
    public async Task<T> UseAsync<T>(
        FirefoxProfileChoice profile, bool named, Func<FirefoxSession, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(operation);

        var slot = SlotOf();
        if (slot is not null)
        {
            var result = await slot.UseAsync(profile, named, OpenAsync, operation, cancellationToken).ConfigureAwait(false);
            if (result is not null)
                return result;
        }

        // No run, or a run that has ended: a session for this action alone.
        var session = await OpenAsync(profile, cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
            return await operation(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes the current run's browser; <c>false</c> when it has none.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An instance member: the tool holds the manager, which keeps it replaceable.")]
    public async Task<bool> CloseAsync(CancellationToken cancellationToken)
    {
        var slot = SlotOf();
        return slot is not null && await slot.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Closes the shared browsers, when there are any; a run's own browser closes with the run.</summary>
    public void Dispose()
    {
        if (_pool is null)
            return;
        var closing = _pool.DisposeAsync();
        if (!closing.IsCompleted)
            closing.AsTask().GetAwaiter().GetResult();
    }

    private Task<FirefoxSession> OpenAsync(FirefoxProfileChoice profile, CancellationToken cancellationToken) =>
        _pool is null
            ? FirefoxSession.OpenAsync(_launcher, profile, _options, _fileSystem, _timings, _logger, cancellationToken)
            : _pool.AttachAsync(profile, cancellationToken);

    private static FirefoxSessionSlot? SlotOf()
    {
        var scope = AgentRunScope.Current;
        if (scope is null)
            return null;

        try
        {
            // The factory runs under the scope's lock: it only allocates. Firefox starts at the first use.
            return scope.GetOrAdd(s_key, static () => new FirefoxSessionSlot());
        }
        catch (ObjectDisposedException)
        {
            // The run ended (a scripted agent's scope ends without waiting for its last calls).
            return null;
        }
    }

    /// <summary>The key of a run's browser: private, so no other resource can hold it. One browser per run.</summary>
    private sealed record SlotKey;

    /// <summary>
    /// The browser of one run: registered in the run's scope, which disposes it when the run ends. It
    /// holds at most one live session at a time, replaced after a reset.
    /// </summary>
    private sealed class FirefoxSessionSlot : IAsyncDisposable
    {
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "An action may still be queued on the gate when the run ends; a SemaphoreSlim without AvailableWaitHandle holds no unmanaged resource.")]
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Lock _sync = new();
        private FirefoxSession? _session;
        private bool _disposed;

        /// <summary>
        /// Runs <paramref name="operation"/> on the slot's session, one at a time, opening it first when
        /// needed; null when the slot has ended.
        /// </summary>
        public async Task<T?> UseAsync<T>(
            FirefoxProfileChoice profile, bool named,
            Func<FirefoxProfileChoice, CancellationToken, Task<FirefoxSession>> open,
            Func<FirefoxSession, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
            where T : class
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                FirefoxSession? session;
                lock (_sync)
                {
                    if (_disposed)
                        return null;
                    session = _session;
                }

                if (session is { IsAlive: false })
                {
                    // The user closed the window, or the browser died: the next page starts afresh.
                    await ResetAsync(session).ConfigureAwait(false);
                    session = null;
                }

                if (session is not null && named && !session.Browser.Profile.Same(profile))
                {
                    throw new FirefoxException(
                        $"The browser of this run runs with the profile '{session.Browser.Profile}': close it before opening one with '{profile}'.");
                }

                if (session is null)
                {
                    session = await open(profile, cancellationToken).ConfigureAwait(false);
                    bool ended;
                    lock (_sync)
                    {
                        ended = _disposed;
                        if (!ended)
                            _session = session;
                    }

                    if (ended)
                    {
                        // The run ended while Firefox was starting: it must not outlive the run.
                        await session.DisposeAsync().ConfigureAwait(false);
                        return null;
                    }
                }

                try
                {
                    return await operation(session, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IExpectedToolError && session.IsAlive)
                {
                    // The agent reads the reason and goes on in the same page.
                    throw;
                }
                catch
                {
                    // Cancelled, or the browser died: the next action starts afresh.
                    await ResetAsync(session).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Closes the slot's session, once the action in progress has ended; false when it has none.</summary>
        public async Task<bool> CloseAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                FirefoxSession? session;
                lock (_sync)
                    session = _session;
                if (session is null)
                    return false;

                await ResetAsync(session).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Ends the slot and its session; an action still running fails with the browser.</summary>
        public async ValueTask DisposeAsync()
        {
            FirefoxSession? session;
            lock (_sync)
            {
                _disposed = true;
                session = _session;
                _session = null;
            }

            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
        }

        private async Task ResetAsync(FirefoxSession session)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_session, session))
                    _session = null;
            }

            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
