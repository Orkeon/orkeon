namespace Orkeon.Cli.Scripting.Dispatch;

/// <summary>
/// Mutable, thread-safe record of one dispatched command, held by the
/// <see cref="CommandInstanceRegistry"/>. Background completion (the agent's response on a
/// pool thread) and the engine thread (reads via <c>ps</c>/drain) both touch it, so every
/// transition is guarded by <see cref="_gate"/>.
/// </summary>
/// <remarks>
/// JS never sees this type directly — <see cref="Snapshot"/> produces the immutable
/// <see cref="CommandInstanceView"/> handed to scripts.
/// </remarks>
public sealed class CommandInstance
{
    private readonly object _gate = new();
    private CommandInstanceState _state = CommandInstanceState.Running;
    private DateTimeOffset? _completedAt;
    private CommandResponse? _result;
    private string? _error;
    private CommandProgress? _progress;

    private Action<CommandInstance>? _terminalCallback;
    private bool _terminalCallbackFired;

    /// <summary>
    /// Registry-owned observer fired once on terminal transition (separate from the runner's
    /// one-shot <see cref="AttachTerminalCallback"/>). Used to drive retention eviction.
    /// </summary>
    internal Action<CommandInstance>? TerminalObserver { get; set; }

    internal CommandInstance(
        string ticket,
        string name,
        CommandInstanceKind kind,
        string targetAgent,
        string intent,
        Guid correlationId,
        DateTimeOffset startedAt)
    {
        Ticket = ticket;
        Name = name;
        Kind = kind;
        TargetAgent = targetAgent;
        Intent = intent;
        CorrelationId = correlationId;
        StartedAt = startedAt;
    }

    /// <summary>Opaque ticket handle.</summary>
    public string Ticket { get; }

    /// <summary>Originating command name.</summary>
    public string Name { get; }

    /// <summary>Sync or async.</summary>
    public CommandInstanceKind Kind { get; }

    /// <summary>Logical name of the addressed agent.</summary>
    public string TargetAgent { get; }

    /// <summary>Intent the command was dispatched with.</summary>
    public string Intent { get; }

    /// <summary>Host-side completion key.</summary>
    public Guid CorrelationId { get; }

    /// <summary>Start timestamp.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Current lifecycle state (thread-safe read).</summary>
    public CommandInstanceState State { get { lock (_gate) return _state; } }

    /// <summary>True once the instance reaches a terminal state.</summary>
    public bool IsTerminal { get { lock (_gate) return _state.IsTerminal(); } }

    /// <summary>Marks the instance done with the agent's successful response. No-op if already terminal.</summary>
    public void Complete(CommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        TransitionTerminal(CommandInstanceState.Done, response, response.error, DateTimeOffset.UtcNow);
    }

    /// <summary>Marks the instance failed with a reason. No-op if already terminal.</summary>
    public void Fail(string error)
        => TransitionTerminal(CommandInstanceState.Failed, null, error, DateTimeOffset.UtcNow);

    /// <summary>Marks the instance cancelled. No-op if already terminal.</summary>
    public void Cancel(string? reason = null)
        => TransitionTerminal(CommandInstanceState.Cancelled, null, reason ?? "cancelled", DateTimeOffset.UtcNow);

    /// <summary>Marks the instance rejected by the admission quota. No-op if already terminal.</summary>
    public void Reject(string reason)
        => TransitionTerminal(CommandInstanceState.Rejected, null, reason, DateTimeOffset.UtcNow);

    /// <summary>Publishes a progress snapshot (ignored once terminal).</summary>
    public void ReportProgress(CommandProgress progress)
    {
        lock (_gate)
        {
            if (_state.IsTerminal()) return;
            _progress = progress;
        }
    }

    private long _tokens;

    /// <summary>
    /// Credits LLM tokens to this instance. Accepted even after a terminal transition —
    /// the usage of the last response legitimately races Complete — so the final count
    /// stays truthful in <c>ps</c>/<c>inspect</c>.
    /// </summary>
    public void AddTokens(long count)
    {
        if (count > 0) Interlocked.Add(ref _tokens, count);
    }

    /// <summary>Total LLM tokens attributed to this instance so far.</summary>
    public long TokensUsed => Interlocked.Read(ref _tokens);

    /// <summary>
    /// Attaches a one-shot callback fired when (or immediately, if already) the instance
    /// reaches a terminal state. Used by the runner to release the admission slot and
    /// queue the <c>completed</c> drain without racing the background completion.
    /// </summary>
    internal void AttachTerminalCallback(Action<CommandInstance> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        bool fireNow;
        lock (_gate)
        {
            if (_terminalCallbackFired)
                return;
            if (_state.IsTerminal())
            {
                _terminalCallbackFired = true;
                fireNow = true;
            }
            else
            {
                _terminalCallback = callback;
                fireNow = false;
            }
        }
        if (fireNow)
            callback(this);
    }

    private void TransitionTerminal(CommandInstanceState target, CommandResponse? result, string? error, DateTimeOffset at)
    {
        Action<CommandInstance>? callback = null;
        lock (_gate)
        {
            if (_state.IsTerminal())
                return;
            _state = target;
            _result = result;
            _error = error;
            _completedAt = at;
            if (_terminalCallback is not null && !_terminalCallbackFired)
            {
                _terminalCallbackFired = true;
                callback = _terminalCallback;
                _terminalCallback = null;
            }
        }
        callback?.Invoke(this);
        TerminalObserver?.Invoke(this);
    }

    /// <summary>Produces an immutable JS-facing snapshot.</summary>
    public CommandInstanceView Snapshot()
    {
        lock (_gate)
        {
            var end = _completedAt ?? DateTimeOffset.UtcNow;
            var elapsed = (long)Math.Max(0, (end - StartedAt).TotalMilliseconds);
            return new CommandInstanceView(
                new CommandInstanceIdentity(
                    Ticket: Ticket,
                    Name: Name,
                    Kind: Kind.ToToken(),
                    TargetAgent: TargetAgent,
                    Intent: Intent,
                    CorrelationId: CorrelationId.ToString()),
                new CommandInstanceLifecycle(
                    State: _state.ToToken(),
                    StartedAt: StartedAt,
                    CompletedAt: _completedAt,
                    ElapsedMs: elapsed,
                    Result: _result,
                    Error: _error,
                    Progress: _progress,
                    Tokens: Interlocked.Read(ref _tokens)));
        }
    }
}
