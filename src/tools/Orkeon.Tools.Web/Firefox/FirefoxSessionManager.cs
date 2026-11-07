using Microsoft.Extensions.Logging;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// Gives each action its browser session. By default (<see cref="FirefoxBrowserLifetime.Run"/>) the
/// session lives in the run's <see cref="AgentRunScope"/>: Firefox starts at the run's first action, the
/// next ones find the page where the last left it, and the session closes with the run. Agents running
/// side by side have their own scopes, hence their own sessions: a browser each by default, tabs of their
/// own in one shared browser per profile with <c>SharedInstance</c>. With
/// <see cref="FirefoxBrowserLifetime.Host"/> the session is the host's: one per profile, kept with its
/// tabs from one run to the next until <c>close</c>, the user closes the window, or the host stops.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Outside any run, an action gets a session of its own, closed right after it - or, under the
/// host's lifetime, the host's session of its profile.</item>
/// <item>A run's browser starts on the profile its first action names, or the host's default; a later
/// action that names another profile is refused until <c>close</c>. Under the host's lifetime, "the run's
/// browser" is the host's browser of that profile, and an action without a profile goes there.</item>
/// <item>An action that fails for a reason the agent can act on (<see cref="IExpectedToolError"/>) keeps
/// the browser; any other failure, a cancellation, or a browser that has gone closes it, and the next
/// action starts a new one.</item>
/// </list>
/// </remarks>
internal sealed class FirefoxSessionManager : IDisposable
{
    private static readonly SlotKey s_key = new();
    private static readonly BindingKey s_bindingKey = new();

    private readonly IFirefoxLauncher _launcher;
    private readonly FirefoxToolOptions _options;
    private readonly IFileSystemService _fileSystem;
    private readonly FirefoxSessionTimings? _timings;
    private readonly ILogger? _logger;
    private readonly FirefoxBrowserPool? _pool;
    private readonly Dictionary<string, FirefoxSessionSlot>? _host;
    private readonly Lock _sync = new();
    private bool _disposed;

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
        var hostLifetime = options.Lifetime == FirefoxBrowserLifetime.Host;
        if (options.SharedInstance)
            _pool = new FirefoxBrowserPool(launcher, options, fileSystem, logger, timings, keepIdle: hostLifetime);
        else if (hostLifetime)
            _host = new Dictionary<string, FirefoxSessionSlot>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The shared browsers now running; <c>null</c> when each run, or the host, has its own.</summary>
    public FirefoxBrowserPool? Pool => _pool;

    /// <summary>How many browsers the host keeps between runs (<c>Lifetime: host</c> without <c>SharedInstance</c>).</summary>
    public int HostBrowserCount
    {
        get
        {
            lock (_sync)
                return _host?.Values.Count(slot => slot.IsOpen) ?? 0;
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> on the session the action goes to, started first when needed on
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

        if (_host is not null)
        {
            // The host's session of the profile, the run bound to it from its first action on.
            var target = BindingOf()?.Bind(profile, named, IsOpen) ?? profile;
            var kept = await HostSlotOf(target).UseAsync(target, named: false, OpenAsync, operation, cancellationToken).ConfigureAwait(false);
            return kept ?? throw new ObjectDisposedException(nameof(FirefoxSessionManager));
        }

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

    /// <summary>
    /// Closes the browser the action addresses: the current run's, or, outside any run, the host's
    /// browser of <paramref name="profile"/>; <c>false</c> when there is none.
    /// </summary>
    /// <exception cref="FirefoxException">Under the host's lifetime, <paramref name="named"/> names a profile other than the run's while the run's browser is open.</exception>
    public async Task<bool> CloseAsync(FirefoxProfileChoice profile, bool named, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_host is not null)
        {
            var target = BindingOf()?.Unbind(profile, named, IsOpen) ?? profile;
            FirefoxSessionSlot? kept;
            lock (_sync)
                _host.TryGetValue(target.Name, out kept);
            return kept is not null && await kept.CloseAsync(cancellationToken).ConfigureAwait(false);
        }

        var slot = SlotOf();
        if (slot is not null)
            return await slot.CloseAsync(cancellationToken).ConfigureAwait(false);

        // Outside any run, a browser the host keeps for the next run is closed on demand.
        return _pool is { KeepsIdle: true } && await _pool.CloseAsync(profile).ConfigureAwait(false);
    }

    /// <summary>Closes the browsers of the host - shared, or kept between runs; a run's own browser closes with the run.</summary>
    public void Dispose()
    {
        List<FirefoxSessionSlot> kept = [];
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_host is not null)
            {
                kept = [.. _host.Values];
                _host.Clear();
            }
        }

        foreach (var slot in kept)
            Wait(slot.DisposeAsync());
        if (_pool is not null)
            Wait(_pool.DisposeAsync());
    }

    private static void Wait(ValueTask closing)
    {
        if (!closing.IsCompleted)
            closing.AsTask().GetAwaiter().GetResult();
    }

    private Task<FirefoxSession> OpenAsync(FirefoxProfileChoice profile, CancellationToken cancellationToken) =>
        _pool is null
            ? FirefoxSession.OpenAsync(_launcher, profile, _options, _fileSystem, _timings, _logger, cancellationToken)
            : _pool.AttachAsync(profile, cancellationToken);

    private FirefoxSessionSlot HostSlotOf(FirefoxProfileChoice profile)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_host!.TryGetValue(profile.Name, out var slot))
            {
                slot = new FirefoxSessionSlot();
                _host[profile.Name] = slot;
            }

            return slot;
        }
    }

    private bool IsOpen(FirefoxProfileChoice profile)
    {
        lock (_sync)
            return _host!.TryGetValue(profile.Name, out var slot) && slot.IsOpen;
    }

    private static FirefoxSessionSlot? SlotOf() => ResourceOf(s_key, static () => new FirefoxSessionSlot());

    private static FirefoxRunBinding? BindingOf() => ResourceOf(s_bindingKey, static () => new FirefoxRunBinding());

    private static T? ResourceOf<T>(object key, Func<T> factory) where T : class, IAsyncDisposable
    {
        var scope = AgentRunScope.Current;
        if (scope is null)
            return null;

        try
        {
            // The factory runs under the scope's lock: it only allocates. Firefox starts at the first use.
            return scope.GetOrAdd(key, factory);
        }
        catch (ObjectDisposedException)
        {
            // The run ended (a scripted agent's scope ends without waiting for its last calls).
            return null;
        }
    }

    /// <summary>The key of a run's browser: private, so no other resource can hold it. One browser per run.</summary>
    private sealed record SlotKey;

    /// <summary>The key of the profile a run is bound to under the host's lifetime.</summary>
    private sealed record BindingKey;

    /// <summary>
    /// The profile a run is bound to under the host's lifetime: the one its first action named, or the
    /// default; released by <c>close</c>, or when the host's browser of that profile has gone.
    /// </summary>
    private sealed class FirefoxRunBinding : IAsyncDisposable
    {
        private readonly Lock _sync = new();
        private FirefoxProfileChoice? _profile;

        /// <summary>
        /// The profile an action goes to: the run's, or <paramref name="profile"/> when the run has none
        /// yet, which binds the run to it. When <paramref name="named"/> names another profile than the
        /// run's, the action is refused while the run's browser is open.
        /// </summary>
        /// <exception cref="FirefoxException">Another profile was named while the run's browser is open.</exception>
        public FirefoxProfileChoice Bind(FirefoxProfileChoice profile, bool named, Func<FirefoxProfileChoice, bool> isOpen)
        {
            lock (_sync)
            {
                if (_profile is { } bound)
                {
                    if (!named || bound.Same(profile))
                        return bound;
                    if (isOpen(bound))
                        throw Refusal(bound, profile);
                }

                _profile = profile;
                return profile;
            }
        }

        /// <summary>The profile <c>close</c> addresses - the run's, else <paramref name="profile"/> - the run unbound from it.</summary>
        /// <exception cref="FirefoxException">Another profile was named while the run's browser is open.</exception>
        public FirefoxProfileChoice Unbind(FirefoxProfileChoice profile, bool named, Func<FirefoxProfileChoice, bool> isOpen)
        {
            lock (_sync)
            {
                var bound = _profile;
                if (bound is not null && named && !bound.Same(profile) && isOpen(bound))
                    throw Refusal(bound, profile);

                _profile = null;
                return bound is not null && (!named || bound.Same(profile)) ? bound : profile;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static FirefoxException Refusal(FirefoxProfileChoice bound, FirefoxProfileChoice asked) =>
            new($"The browser of this run runs with the profile '{bound}': close it before opening one with '{asked}'.");
    }
}
