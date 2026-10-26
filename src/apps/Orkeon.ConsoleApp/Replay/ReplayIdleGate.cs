using System.Collections.Concurrent;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Dispatch;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>Why a replay stops: a line that failed, or a background task it launched that ended failed.</summary>
/// <param name="LineNumber">The replay line charged with it: the line that ran, or launched, what failed.</param>
/// <param name="Cause">What the stop line says after the number.</param>
internal sealed record ReplayFailure(int LineNumber, string Cause);

/// <summary>Who reads a replay line: the read loop, or a command that holds the loop (<c>runCrew</c>'s approvals).</summary>
internal enum ReplayReader
{
    /// <summary>The REPL's read loop (<see cref="ReplayConsoleAdapter.ReadLineAsync"/>).</summary>
    Loop,

    /// <summary>A synchronous read from inside a command (<see cref="ReplayConsoleAdapter.ReadLine"/>).</summary>
    Command,
}

/// <summary>
/// What a replay waits for before it feeds its next line: no command instance still running in the
/// <see cref="CommandInstanceRegistry"/> - the background crews and tasks the TUI's task rows list come from
/// it (<c>TuiFidelityWiring.BuildAgentRows</c>) -, unless a question waits for an answer (a tool approval of
/// <see cref="IToolApprovalWatch"/>, or a <c>human_input</c> question of the read loop's interceptor): the
/// next line then answers it, so the wait cannot deadlock.
/// <para>
/// One tool approval consumes exactly one line, whoever reads it: the read loop (the approval channel's
/// interceptor) and a command that answers its crew's questions itself (<c>runCrew</c>'s
/// <c>AnswerFromConsole</c>, from a background task too) may both want it. The reader that takes a line for a
/// pending approval holds a claim on it (<see cref="WaitAsync"/>, <see cref="WaitForCommandRead"/>) until the
/// question claimed is answered or withdrawn, or that reader reads again; the other reader waits meanwhile. A new
/// question, or the withdrawal of another one, leaves the claim in place.
/// </para>
/// <para>
/// The wait wakes on the registry's <see cref="CommandInstanceRegistry.WorkSettled"/> and
/// <see cref="CommandInstanceRegistry.InstanceTerminated"/>, and on the approvals'
/// <see cref="IToolApprovalWatch.PendingChanged"/>. The registry counts an instance as running until its terminal
/// observers - this gate's failure record among them - have run, and a <c>completed()</c> drain until it ended:
/// the wait that sees no work in flight has seen every failure, without relying on a timer. A <c>human_input</c>
/// question raises no signal: the wait looks again every <see cref="RecheckInterval"/>, a cheap read, cancellable.
/// </para>
/// <para>
/// It also records the background instances launched while a line is tracked (<see cref="TrackingLine"/>):
/// one that ends <see cref="CommandInstanceState.Failed"/> is a failure of that line. An instance launched by a
/// <c>completed()</c> callback is charged to the line in flight when it registers, not to the line that
/// launched its parent.
/// </para>
/// </summary>
internal sealed class ReplayIdleGate : IDisposable
{
    /// <summary>How often the wait looks again for a question that raised no signal.</summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<CommandInstanceRegistry?> _registryFactory;
    private readonly Func<IToolApprovalWatch?> _approvalsFactory;
    private readonly Func<bool> _otherQuestionPending;
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, int> _launchedAt = new(StringComparer.Ordinal);
    private TaskCompletionSource _changed = NewSignal();
    private ReplayFailure? _failure;
    private ReplayReader? _claim;
    private PendingToolApproval? _claimed;
    private CommandInstanceRegistry? _registry;
    private IToolApprovalWatch? _approvals;
    private bool _attached;
    private volatile int _trackingLine;
    private int _commandReadsWaiting;

    /// <summary>Creates the gate; the sources are resolved at the first <see cref="Attach"/>.</summary>
    /// <param name="registry">The registry of the command instances; null when the host has none.</param>
    /// <param name="approvals">The tool approvals waiting; null when the host has none.</param>
    /// <param name="otherQuestionPending">Whether another question (a <c>human_input</c>) waits; null for none.</param>
    public ReplayIdleGate(
        Func<CommandInstanceRegistry?> registry,
        Func<IToolApprovalWatch?> approvals,
        Func<bool>? otherQuestionPending = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(approvals);
        _registryFactory = registry;
        _approvalsFactory = approvals;
        _otherQuestionPending = otherQuestionPending ?? (static () => false);
    }

    /// <summary>
    /// The replay line in flight, which the background instances registered from now on are charged to; 0 when
    /// no line is tracked (no replay runs), and new instances are not recorded.
    /// </summary>
    public int TrackingLine
    {
        get => _trackingLine;
        set => _trackingLine = value;
    }

    /// <summary>Whether a question waits for the next line.</summary>
    public bool QuestionPending => ApprovalPending || _otherQuestionPending();

    /// <summary>Whether a tool approval waits for the next line.</summary>
    public bool ApprovalPending => (_approvals?.Pending.Count ?? 0) > 0;

    /// <summary>The tool of the first approval waiting; null when none waits.</summary>
    public string? PendingApprovalTool => _approvals?.Pending is { Count: > 0 } pending ? pending[0].ToolName : null;

    /// <summary>Whether another question (a <c>human_input</c>) waits for the next line.</summary>
    public bool OtherQuestionPending => _otherQuestionPending();

    /// <summary>Whether a command instance still runs.</summary>
    public bool WorkRunning => _registry?.HasRunning ?? false;

    /// <summary>The reader that holds the claim on the approval waiting; null when none does.</summary>
    public ReplayReader? Claim
    {
        get
        {
            lock (_gate)
                return _claim;
        }
    }

    /// <summary>How many command reads wait for the read loop's claim to end (<see cref="WaitForCommandRead"/>).</summary>
    public int CommandReadsWaiting => Volatile.Read(ref _commandReadsWaiting);

    /// <summary>Resolves the sources and subscribes to their signals, once.</summary>
    public void Attach()
    {
        lock (_gate)
        {
            if (_attached)
                return;
            _attached = true;
        }

        _registry = _registryFactory();
        _approvals = _approvalsFactory();
        if (_registry is not null)
        {
            _registry.InstanceRegistered += OnRegistered;
            _registry.InstanceTerminated += OnTerminated;
            _registry.WorkSettled += OnWorkSettled;
        }

        if (_approvals is not null)
            _approvals.PendingChanged += OnPendingChanged;
    }

    /// <summary>
    /// The read loop's wait. Completes when the next line may be fed: a background failure was recorded, an
    /// approval waits that no command is answering (the loop then holds the claim), another question waits, or no
    /// instance runs. An approval a command holds the claim on keeps the loop waiting: the line goes to the
    /// command. The loop's own claim, from its previous line, is released first.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait (Ctrl-C, the end of the REPL).</param>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Attach();
        Release(ReplayReader.Loop);
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                changed = _changed.Task;
                if (_failure is not null)
                    return;
                if (FirstPending() is { } head)
                {
                    if (_claim is null)
                    {
                        Take(ReplayReader.Loop, head);
                        return;
                    }
                }
                else if (_otherQuestionPending() || !WorkRunning)
                {
                    return;
                }
            }

            try
            {
                await changed.WaitAsync(RecheckInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A question without a signal (human_input): look again.
            }
        }
    }

    /// <summary>
    /// A command's synchronous read. True when it may take the next line: no approval waits (a plain read), or one
    /// waits that the loop does not hold (the command then holds the claim). While the loop holds the claim, the
    /// read waits for the approvals to change; false when the approval it waited for was answered meanwhile (no
    /// line is taken), or when <paramref name="cancellationToken"/> fired. The command's own claim, from its
    /// previous line, is released first.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait (Ctrl-C).</param>
    public bool WaitForCommandRead(CancellationToken cancellationToken)
    {
        Attach();
        Release(ReplayReader.Command);
        var waited = false;
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                changed = _changed.Task;
                if (FirstPending() is not { } head)
                    return !waited;
                if (_claim is null)
                {
                    Take(ReplayReader.Command, head);
                    return true;
                }
            }

            waited = true;
            Interlocked.Increment(ref _commandReadsWaiting);
            try
            {
                changed.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                Interlocked.Decrement(ref _commandReadsWaiting);
            }
        }
    }

    /// <summary>Releases the claim <paramref name="reader"/> holds, if any: its line was dealt with.</summary>
    public void Release(ReplayReader reader)
    {
        bool released;
        lock (_gate)
        {
            released = _claim == reader;
            if (released)
                ClearClaim();
        }

        if (released)
            Signal();
    }

    /// <summary>Whether a background failure waits to be taken.</summary>
    public bool HasFailure
    {
        get
        {
            lock (_gate)
                return _failure is not null;
        }
    }

    /// <summary>Takes the background failure recorded, the first one; null when none.</summary>
    public ReplayFailure? TakeFailure()
    {
        lock (_gate)
        {
            var failure = _failure;
            _failure = null;
            return failure;
        }
    }

    /// <summary>Forgets the instances recorded, any failure and any claim: a replay ended.</summary>
    public void Reset()
    {
        TrackingLine = 0;
        _launchedAt.Clear();
        lock (_gate)
        {
            _failure = null;
            ClearClaim();
        }

        Signal();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_registry is not null)
        {
            _registry.InstanceRegistered -= OnRegistered;
            _registry.InstanceTerminated -= OnTerminated;
            _registry.WorkSettled -= OnWorkSettled;
        }

        if (_approvals is not null)
            _approvals.PendingChanged -= OnPendingChanged;
    }

    private void OnRegistered(object? sender, CommandInstanceEventArgs e)
    {
        var instance = e.Instance;
        if (instance.Kind == CommandInstanceKind.Async && TrackingLine is > 0 and var line)
            _launchedAt[instance.Ticket] = line;
        Signal();
    }

    private void OnTerminated(object? sender, CommandInstanceEventArgs e)
    {
        var instance = e.Instance;
        if (_launchedAt.TryRemove(instance.Ticket, out var line) && instance.State == CommandInstanceState.Failed)
        {
            var view = instance.Snapshot();
            var cause = $"[{view.ticket}] {view.name} failed: {view.error ?? "no error given"}";
            lock (_gate)
                _failure ??= new ReplayFailure(line, cause);
        }

        Signal();
    }

    // The approvals changed: the claim is over only when the question claimed left the queue (answered, a session-wide
    // answer included, or withdrawn); a new question, or another one withdrawn, keeps it. Both readers look again.
    private void OnPendingChanged(object? sender, EventArgs e)
    {
        var pending = _approvals?.Pending ?? [];
        lock (_gate)
        {
            if (_claimed is { } claimed && !pending.Contains(claimed))
                ClearClaim();
        }

        Signal();
    }

    private void OnWorkSettled(object? sender, EventArgs e) => Signal();

    private PendingToolApproval? FirstPending() => _approvals?.Pending is { Count: > 0 } pending ? pending[0] : null;

    // Under _gate.
    private void Take(ReplayReader reader, PendingToolApproval question)
    {
        _claim = reader;
        _claimed = question;
    }

    // Under _gate.
    private void ClearClaim()
    {
        _claim = null;
        _claimed = null;
    }

    private void Signal()
    {
        TaskCompletionSource fired;
        lock (_gate)
        {
            fired = _changed;
            _changed = NewSignal();
        }

        fired.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
