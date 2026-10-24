using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.Execution;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// Gives each agent run its shell session. The session lives in the run's
/// <see cref="AgentRunScope"/>: opened at the run's first command, reused by the next ones, ended
/// with the run. Agents running side by side have their own scopes, hence their own sessions.
/// Stateless itself, so one instance serves every tool.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Outside any run, a command gets a session of its own, disposed right after it.</item>
/// <item>A timeout or the caller's cancellation kills the session; the next command opens a new one,
/// and the timed-out result says so (<see cref="ShellRunResult.SessionReset"/>).</item>
/// <item>A command whose view differs from the session's (the ambient file-system scope changed the
/// mounts) closes the session and opens one in the new view.</item>
/// </list>
/// </remarks>
internal sealed class ShellSessionManager
{
    /// <summary>The instance a tool built without dependency injection uses.</summary>
    public static ShellSessionManager Shared { get; } = new();

    /// <summary>Runs <paramref name="invocation"/> in the current run's session of <paramref name="sandbox"/>.</summary>
    /// <param name="sandbox">How the session is run.</param>
    /// <param name="view">The view the command needs.</param>
    /// <param name="invocation">The command, validated by the tool.</param>
    /// <param name="cancellationToken">The caller's cancellation, which kills the session.</param>
    /// <exception cref="ShellSessionException">The session could not start or died.</exception>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An instance member: the tool receives the manager by injection, which keeps it replaceable.")]
    public async Task<ShellRunResult> RunAsync(
        IShellSandbox sandbox, ShellSessionView view, ShellInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(invocation);

        var slot = SlotOf(sandbox);
        if (slot is not null)
        {
            var result = await slot.RunAsync(sandbox, view, invocation, cancellationToken).ConfigureAwait(false);
            if (result is not null)
                return result;
        }

        // No run, or a run that has ended: a session for this command alone.
        var session = await sandbox.OpenAsync(view, cancellationToken).ConfigureAwait(false);
        await using (session.ConfigureAwait(false))
            return await session.RunAsync(invocation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts <paramref name="invocation"/> detached in the current run's session of <paramref name="sandbox"/>.</summary>
    /// <param name="sandbox">How the session is run.</param>
    /// <param name="view">The view the command needs.</param>
    /// <param name="invocation">The command, validated by the tool.</param>
    /// <param name="physicalLogPath">The host path the job's output goes to.</param>
    /// <param name="openLogAsync">Opens the job log for writing through the VFS.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <exception cref="InvalidOperationException">No run holds the session, or the run has ended.</exception>
    /// <exception cref="ShellSessionException">The session could not start or died.</exception>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An instance member: the tool receives the manager by injection, which keeps it replaceable.")]
    public async Task<ShellJobHandle> StartDetachedAsync(
        IShellSandbox sandbox, ShellSessionView view, ShellInvocation invocation, string physicalLogPath, Func<CancellationToken, Task<Stream>> openLogAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(invocation);

        var slot = SlotOf(sandbox);
        return slot is null
            ? throw new InvalidOperationException("A job needs a running agent run.")
            : await slot.UseAsync(sandbox, view, (session, token) => session.StartDetachedAsync(invocation, physicalLogPath, openLogAsync, token), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The agent run has ended.");
    }

    private static ShellSessionSlot? SlotOf(IShellSandbox sandbox)
    {
        var scope = AgentRunScope.Current;
        if (scope is null)
            return null;

        try
        {
            return scope.GetOrAdd(new SlotKey(sandbox.Name), () => new ShellSessionSlot());
        }
        catch (ObjectDisposedException)
        {
            // The run ended (a scripted agent's scope ends without waiting for its last calls).
            return null;
        }
    }

    /// <summary>The key of a run's session of one kind of sandbox: private, so no other resource can hold it.</summary>
    private sealed record SlotKey(string SandboxName);

    /// <summary>
    /// The session of one run and one kind of sandbox: registered in the run's scope, which disposes it
    /// when the run ends. It holds at most one live session at a time, replaced after a reset.
    /// </summary>
    private sealed class ShellSessionSlot : IAsyncDisposable
    {
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A command may still be queued on the gate when the run ends; a SemaphoreSlim without AvailableWaitHandle holds no unmanaged resource.")]
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _sync = new();
        private IShellSession? _session;
        private string? _fingerprint;
        private bool _disposed;

        /// <summary>Runs the command in the slot's session, or returns null when the slot has ended.</summary>
        public Task<ShellRunResult?> RunAsync(
            IShellSandbox sandbox, ShellSessionView view, ShellInvocation invocation, CancellationToken cancellationToken)
            => UseAsync(sandbox, view, async (session, token) =>
            {
                var result = await session.RunAsync(invocation, token).ConfigureAwait(false);
                if (!result.TimedOut)
                    return result;

                await ResetAsync(session).ConfigureAwait(false);
                return result with { SessionReset = true };
            }, cancellationToken);

        /// <summary>
        /// Runs <paramref name="operation"/> on the slot's session, one at a time, opening it first when
        /// needed; null when the slot has ended. An operation that throws leaves the session reset.
        /// </summary>
        public async Task<T?> UseAsync<T>(
            IShellSandbox sandbox, ShellSessionView view, Func<IShellSession, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
            where T : class
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IShellSession? session;
                lock (_sync)
                {
                    if (_disposed)
                        return null;
                    session = _session;
                }

                if (session is not null && !string.Equals(_fingerprint, view.Fingerprint, StringComparison.Ordinal))
                {
                    await ResetAsync(session).ConfigureAwait(false);
                    session = null;
                }

                if (session is null)
                {
                    session = await sandbox.OpenAsync(view, cancellationToken).ConfigureAwait(false);
                    bool ended;
                    lock (_sync)
                    {
                        ended = _disposed;
                        if (!ended)
                        {
                            _session = session;
                            _fingerprint = view.Fingerprint;
                        }
                    }

                    if (ended)
                    {
                        // The run ended while the session was starting: it must not outlive the run.
                        await session.DisposeAsync().ConfigureAwait(false);
                        return null;
                    }
                }

                try
                {
                    return await operation(session, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Cancelled, or the shell died: the next command starts afresh.
                    await ResetAsync(session).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Ends the slot and its session; a command still running fails with the session.</summary>
        public async ValueTask DisposeAsync()
        {
            IShellSession? session;
            lock (_sync)
            {
                _disposed = true;
                session = _session;
                _session = null;
            }

            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
        }

        private async Task ResetAsync(IShellSession session)
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
