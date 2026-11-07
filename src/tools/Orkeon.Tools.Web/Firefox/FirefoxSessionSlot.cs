using System.Diagnostics.CodeAnalysis;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// One browser session and the gate its actions go through, one at a time. A run's slot is registered in
/// the run's scope, which disposes it when the run ends; a host's slot (<c>Lifetime: host</c>) is one per
/// profile, disposed with the host. It holds at most one live session at a time, replaced after a reset.
/// </summary>
/// <param name="keepOnFailure">
/// <c>true</c> keeps the session when an action fails or is cancelled, as long as its browser is there
/// (the host's browser outlives the actions); <c>false</c> closes it, and the next action starts afresh.
/// </param>
internal sealed class FirefoxSessionSlot(bool keepOnFailure = false) : IAsyncDisposable
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "An action may still be queued on the gate when the slot ends; a SemaphoreSlim without AvailableWaitHandle holds no unmanaged resource.")]
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private FirefoxSession? _session;
    private bool _disposed;

    /// <summary>
    /// <c>true</c> while the slot holds a session whose browser is still there, or while an action is
    /// opening or using one: what a caller must not take for free.
    /// </summary>
    public bool IsOpen
    {
        get
        {
            lock (_sync)
                return _session is { IsAlive: true } || _gate.CurrentCount == 0;
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> on the slot's session, one at a time, opening it first when
    /// needed; null when the slot has ended. When <paramref name="named"/>, the caller asked for
    /// <paramref name="profile"/>: a session already open on another one refuses.
    /// </summary>
    /// <exception cref="FirefoxException">Firefox is not installed, did not start, or the profile is missing, held, or not the session's.</exception>
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
                    // The slot ended while Firefox was starting: the browser must not outlive it.
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
            catch when (!keepOnFailure || !session.IsAlive)
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

    /// <summary>
    /// Closes the slot's session, once the action in progress has ended; false when it has none, or
    /// when its browser had gone already.
    /// </summary>
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

            var alive = session.IsAlive;
            await ResetAsync(session).ConfigureAwait(false);
            return alive;
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
