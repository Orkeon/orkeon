using System.Collections.Concurrent;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Dispatch;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>Why a replay stops: a line that failed, or a background task it launched that ended failed.</summary>
/// <param name="LineNumber">The replay line charged with it: the line that ran, or launched, what failed.</param>
/// <param name="Cause">What the stop line says after the number.</param>
internal sealed record ReplayFailure(int LineNumber, string Cause);

/// <summary>
/// What a replay waits for before it feeds its next line: no command instance still running in the
/// <see cref="CommandInstanceRegistry"/> — the background crews and tasks the TUI's task rows list come from
/// it (<c>TuiFidelityWiring.BuildAgentRows</c>) —, unless a question waits for an answer (a tool approval of
/// <see cref="IToolApprovalWatch"/>, or a <c>human_input</c> question of the read loop's interceptor): the
/// next line then answers it, so the wait cannot deadlock.
/// <para>
/// The wait wakes on the registry's <see cref="CommandInstanceRegistry.InstanceTerminated"/> and on the
/// approvals' <see cref="IToolApprovalWatch.PendingChanged"/>. A <c>human_input</c> question raises no
/// signal: the wait looks again every <see cref="RecheckInterval"/>, a cheap read, cancellable.
/// </para>
/// <para>
/// It also records the background instances launched while a line is tracked (<see cref="TrackingLine"/>):
/// one that ends <see cref="CommandInstanceState.Failed"/> is a failure of that line.
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
    private CommandInstanceRegistry? _registry;
    private IToolApprovalWatch? _approvals;
    private bool _attached;
    private volatile int _trackingLine;

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
    public bool QuestionPending => (_approvals?.Pending.Count ?? 0) > 0 || _otherQuestionPending();

    /// <summary>Whether a command instance still runs.</summary>
    public bool WorkRunning => _registry?.HasRunning ?? false;

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
        }

        if (_approvals is not null)
            _approvals.PendingChanged += OnPendingChanged;
    }

    /// <summary>
    /// Completes when the next line may be fed: no instance runs, a question waits, or a background failure
    /// was recorded.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait (Ctrl-C, the end of the REPL).</param>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Attach();
        while (true)
        {
            Task changed;
            lock (_gate)
                changed = _changed.Task;

            if (QuestionPending || !WorkRunning || HasFailure)
                return;

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

    /// <summary>Forgets the instances recorded and any failure: a replay ended.</summary>
    public void Reset()
    {
        TrackingLine = 0;
        _launchedAt.Clear();
        lock (_gate)
            _failure = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_registry is not null)
        {
            _registry.InstanceRegistered -= OnRegistered;
            _registry.InstanceTerminated -= OnTerminated;
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

    private void OnPendingChanged(object? sender, EventArgs e) => Signal();

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
