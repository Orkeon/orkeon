using System.Diagnostics;
using Jint.Native;
using Orkeon.Constants.Llm;
using Orkeon.Domain.Autonomous;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Telemetry;

namespace Orkeon.Scripting.Runtime;

/// <summary>Which of the crew's three run functions opened a <see cref="CrewRunScope"/>.</summary>
internal enum CrewRunKind { Run, Stream, Agent }

/// <summary>
/// One run's CLR state (SCR-25 T4): the linked cancellation, the budget, the crew span, the agent
/// snapshot, the results, and the bookkeeping of everything the CLR owns while the JavaScript loop
/// drives the run — held instance semaphores, open agent steps, open attempts. Every helper the
/// loop calls is bound to its scope, and the scope is a state machine <c>Running → Ended | Abandoned</c>:
/// after <see cref="End"/> (the loop's own <c>finally</c>) or <see cref="Abandon"/> (a CLR root pump
/// that gave up first) the enders are no-ops and the openers throw, so a continuation stranded by an
/// abandoned drain — which Jint runs under the next drain of the same engine — unwinds instead of
/// opening a new attempt on a run that no longer exists.
/// </summary>
/// <remarks>
/// JavaScript sees the scope as an opaque handle: it is created by <c>open</c> (a script caller) or
/// by <see cref="JsCrew.RunAsync"/> (a CLR caller, which then owns the token sources and disposes them
/// once its pump has returned), handed to every helper, and never read.
/// </remarks>
internal sealed class CrewRunScope : IDisposable
{
    /// <summary>
    /// How long a cancelled run may take to unwind cooperatively — its <c>finally</c> blocks,
    /// <c>onCrewError</c> — before a CLR root pump abandons the engine state and releases what the
    /// CLR owns itself. A run whose awaits observe <c>ctx.signal</c> unwinds in microseconds; this
    /// bounds the ones that do not. A cleanup grace (the <c>HostOptions.ShutdownTimeout</c> shape),
    /// not a ceiling on work.
    /// </summary>
    internal static readonly TimeSpan CancellationGrace = TimeSpan.FromSeconds(5);

    // Running is the enum's default: a scope is running from construction on.
    private enum RunState { Running, Ended, Abandoned }

    private readonly JsCrew _crew;
    private readonly bool _ownedByHost;
    private readonly CancellationTokenSource? _timeout;
    private readonly CancellationTokenSource _linked;
    private readonly CancellationTokenSource _abandon;
    private readonly TaskCompletionSource<JsValue> _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _cancelledRegistration;
    private readonly CancellationTokenRegistration _graceRegistration;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, JsAgent> _held = new(StringComparer.Ordinal);
    private readonly HashSet<AgentStep> _steps = new();
    private readonly HashSet<AgentAttempt> _attempts = new();
    private readonly List<JsTaskResult> _results = new();
    private string _aggregate = string.Empty;
    private RunState _state;
    private bool _started;

    /// <summary>
    /// Constructed with the parsed options so a bad <c>timeout</c> string fails before anything is
    /// allocated (and before <c>Invoke</c>, for a CLR caller). Every token-related member exists from
    /// construction on; <see cref="Start"/> adds only what a run allocates.
    /// </summary>
    internal CrewRunScope(JsCrew crew, CrewRunKind kind, CancellationToken? signal, TimeSpan? timeout,
        bool ownedByHost, CancellationToken externalCt)
    {
        _crew = crew;
        Kind = kind;
        _ownedByHost = ownedByHost;
        _timeout = timeout is null ? null : new CancellationTokenSource(timeout.Value);
        _linked = CancellationTokenSource.CreateLinkedTokenSource(
            externalCt, signal ?? CancellationToken.None, _timeout?.Token ?? CancellationToken.None);
        _abandon = new CancellationTokenSource();
        Token = _linked.Token;
        AbandonToken = _abandon.Token;

        // Both registrations run on the cancelling thread and touch CLR state only: the faulted
        // task is what Jint turns into the rejection of the loop's `stop` promise (a cancelled task
        // would surface as Jint's own ExecutionCanceledException instead), enqueued on the engine's
        // loop by Jint's task bridge and run by whoever drains next — never here.
        _cancelledRegistration = Token.Register(
            static (state, token) => ((CrewRunScope)state!)._cancelled.TrySetException(new OperationCanceledException(token)),
            this);
        _graceRegistration = Token.Register(static (state, _) => ((CrewRunScope)state!).ScheduleAbandon(), this);
    }

    internal CrewRunKind Kind { get; }

    /// <summary>The run's own cancellation: external token, <c>options.signal</c> and <c>options.timeout</c> linked.</summary>
    internal CancellationToken Token { get; }

    /// <summary>Fires <see cref="CancellationGrace"/> after <see cref="Token"/>: the bound of a CLR root pump's drain.</summary>
    internal CancellationToken AbandonToken { get; }

    /// <summary>Faults with an <see cref="OperationCanceledException"/> when <see cref="Token"/> fires; never completes otherwise.</summary>
    internal Task<JsValue> Cancelled => _cancelled.Task;

    /// <summary>Typed budget of this run (<see cref="Start"/>, <see cref="CrewRunKind.Run"/>/<see cref="CrewRunKind.Stream"/> only); null without a budget spec.</summary>
    internal AgentExecutionBudget? Budget { get; private set; }

    /// <summary>The <c>crew.run</c> span (<see cref="Start"/>, listener permitting).</summary>
    internal Activity? Activity { get; private set; }

    /// <summary>The agents this run walks, snapshotted by <see cref="Start"/>; empty for <see cref="CrewRunKind.Agent"/>.</summary>
    internal JsAgent[] Agents { get; private set; } = [];

    internal bool IsRunning
    {
        get { lock (_sync) return _state == RunState.Running; }
    }

    /// <summary>
    /// Allocates what a run needs: the budget (per run, so its wall-time clock starts with the run),
    /// the crew span with its tags (parent: whatever <see cref="System.Diagnostics.Activity.Current"/> is on the
    /// calling thread — the CLR caller's ambient activity, or the drainer's for a script-initiated
    /// run), the once-per-crew shape warning, and the agent snapshot — so an agent spawned or
    /// removed during the run does not disturb the walk. Nothing of this for <c>runAgent</c>.
    /// </summary>
    internal void Start()
    {
        lock (_sync)
        {
            ThrowIfNotRunningCore();
            if (_started) throw new InvalidOperationException($"crew '{_crew.name}': this run has already started.");
            _started = true;
        }
        if (Kind == CrewRunKind.Agent) return;

        Budget = JsBudgetBridge.FromSpec(_crew.Budget);
        Activity = ScriptingActivitySource.Instance.StartActivity(ScriptingActivitySource.CrewRunSpan);
        Activity?.SetTag("orkeon.crew.name", _crew.name);
        Activity?.SetTag("orkeon.crew.process", _crew.Process);
        _crew.WarnOnceAboutDeclarativeOnlyDeclarations();
        Agents = _crew.SnapshotAgents();
    }

    /// <summary>
    /// The opener's guard: an <see cref="OperationCanceledException"/> carrying <see cref="Token"/>
    /// when the run was cancelled, an <see cref="InvalidOperationException"/> when it merely ended.
    /// </summary>
    internal void ThrowIfNotRunning()
    {
        lock (_sync) ThrowIfNotRunningCore();
    }

    private void ThrowIfNotRunningCore()
    {
        if (_state == RunState.Running) return;
        throw NotRunning();
    }

    private Exception NotRunning() => Token.IsCancellationRequested
        ? new OperationCanceledException(Token)
        : new InvalidOperationException($"crew '{_crew.name}': this run has ended.");

    /// <summary>
    /// Takes the agent's instance semaphore for this run. Only the wait observes the token, so the
    /// loop never reaches <c>release</c> while the acquisition is undecided; an acquisition that
    /// lands after the run ended or was abandoned hands the semaphore straight back and faults.
    /// </summary>
    internal async Task<JsValue> AcquireAsync(JsAgent agent)
    {
        ThrowIfNotRunning();
        var sem = _crew.GetInstanceSemaphore(agent);
        await sem.WaitAsync(Token).ConfigureAwait(false);
        lock (_sync)
        {
            if (_state == RunState.Running && _held.TryAdd(agent.id, agent)) return JsValue.Undefined;
        }
        sem.Release();
        throw new OperationCanceledException(Token);
    }

    /// <summary>Hands the agent's instance semaphore back; a no-op when this run does not hold it.</summary>
    internal void Release(JsAgent agent)
    {
        lock (_sync)
        {
            if (!_held.Remove(agent.id)) return;
        }
        _crew.GetInstanceSemaphore(agent).Release();
    }

    /// <summary>
    /// Opens the agent's step: one <c>invoke_agent</c> span per agent, spanning every attempt, an
    /// explicit child of the crew span so parentage holds when two runs interleave on one drainer
    /// (<see cref="System.Diagnostics.Activity.Current"/> is one thread-wide value); the span is
    /// nevertheless current on the calling thread for the LLM facade's own spans to nest under.
    /// </summary>
    internal AgentStep BeginAgent(JsAgent agent)
    {
        ThrowIfNotRunning();
        Activity? activity = null;
        if (JsCrew.HasBody(agent))
        {
            activity = ScriptingActivitySource.Instance.StartActivity(
                GenAiAttributes.SpanName(ScriptingActivitySource.AgentRunSpan, agent.name),
                ActivityKind.Internal,
                parentContext: Activity?.Context ?? default);
            activity?.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationInvokeAgent);
            activity?.SetTag(GenAiAttributes.AgentName, agent.name);
            activity?.SetTag(GenAiAttributes.AgentId, agent.id);
        }
        var step = new AgentStep(agent, activity, DateTime.UtcNow);
        lock (_sync)
        {
            if (_state != RunState.Running)
            {
                activity?.Dispose();
                throw NotRunning();
            }
            _steps.Add(step);
        }
        return step;
    }

    /// <summary>Closes the agent's step and its span; idempotent.</summary>
    internal void EndAgent(AgentStep step)
    {
        lock (_sync)
        {
            if (step.Ended) return;
            step.Ended = true;
            _steps.Remove(step);
        }
        step.Activity?.Dispose();
    }

    /// <summary>
    /// Opens one attempt of the agent's body: a fresh <see cref="JsAgentContext"/> over this run's
    /// token and budget (its state proxy is engine work, legal here on the draining thread), and the
    /// broker's current-agent attribution, remembered so <see cref="CloseAttempt"/> can restore it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The context is owned by the AgentAttempt handle this returns: CloseAttempt disposes it in the loop's finally, and ReleaseEverything (End/Abandon) disposes any attempt still open. The catch below disposes it on every path that does not hand the handle out.")]
    internal AgentAttempt OpenAttempt(JsAgent agent, JsValue initialState, int attempt)
    {
        ThrowIfNotRunning();
        var ctx = new JsAgentContext(_crew.CreateEnvironment(agent, Budget, Token), initialState);
        try
        {
            var previous = _crew.BeginBrokerScope(agent, Token);
            var handle = new AgentAttempt(agent, ctx, previous, attempt);
            lock (_sync)
            {
                if (_state != RunState.Running)
                {
                    _crew.EndBrokerScope(previous);
                    throw NotRunning();
                }
                _attempts.Add(handle);
            }
            return handle;
        }
        catch
        {
            ctx.Dispose();
            throw;
        }
    }

    /// <summary>Closes an attempt: the broker attribution goes back to its previous owner, the context is disposed; idempotent.</summary>
    internal void CloseAttempt(AgentAttempt attempt)
    {
        lock (_sync)
        {
            if (attempt.Closed) return;
            attempt.Closed = true;
            _attempts.Remove(attempt);
        }
        _crew.EndBrokerScope(attempt.Previous);
        attempt.ctx.Dispose();
    }

    /// <summary>
    /// Records an agent's output: the task result carries the CLR value (<c>undefined</c>, <c>null</c>
    /// and a skipped body all record <c>null</c>), the aggregate is the last non-null output's text.
    /// The duration spans every attempt. A no-op once the run ended.
    /// </summary>
    internal void Record(AgentStep step, JsValue output)
    {
        lock (_sync)
        {
            if (_state != RunState.Running) return;
        }
        var clr = output.ToObject();
        _results.Add(new JsTaskResult(step.Agent.name, clr, (DateTime.UtcNow - step.StartUtc).TotalMilliseconds));
        if (clr is not null) _aggregate = clr.ToString() ?? _aggregate;
    }

    internal JsCrewResult Finish() => new(_aggregate, new Dictionary<string, object?>(), _results);

    /// <summary>
    /// The loop's own end, from its <c>finally</c> on the engine thread: releases everything still
    /// open (defensively — a run that ends normally has closed it all), stops the crew span, and
    /// drops the registrations. A host-owned scope keeps its token sources for <see cref="Dispose"/>:
    /// the pump is still inside its drain on <see cref="AbandonToken"/> at this instant.
    /// </summary>
    internal void End()
    {
        if (!TransitionTo(RunState.Ended)) return;
        ReleaseEverything();
        _cancelledRegistration.Dispose();
        _graceRegistration.Dispose();
        if (_ownedByHost) return;
        _abandon.Dispose();
        _linked.Dispose();
        _timeout?.Dispose();
    }

    /// <summary>
    /// The host's end, from the pump's <c>finally</c> when the drain gave up before the loop's own
    /// <c>finally</c> ran (the grace expired, the sandbox erupted, a non-bridged binding threw raw):
    /// cancels the linked token first — so a stranded <c>wait</c> rejects and pending acquisitions
    /// and delays fault under a later drain — then releases what the CLR owns, from this thread and
    /// without touching the engine. A no-op after <see cref="End"/>; tolerates a run never started.
    /// </summary>
    internal void Abandon()
    {
        if (!TransitionTo(RunState.Abandoned)) return;
        _linked.Cancel();
        var (attempts, steps, held) = ReleaseEverything();
        _cancelledRegistration.Dispose();
        _graceRegistration.Dispose();
        _crew.LogRunAbandoned(attempts, steps, held);
    }

    /// <summary>Host owner only, after <see cref="Abandon"/>: registrations first, so a late grace timer cannot reach a disposed source.</summary>
    public void Dispose()
    {
        _cancelledRegistration.Dispose();
        _graceRegistration.Dispose();
        _abandon.Dispose();
        _linked.Dispose();
        _timeout?.Dispose();
    }

    private bool TransitionTo(RunState next)
    {
        lock (_sync)
        {
            if (_state != RunState.Running) return false;
            _state = next;
            return true;
        }
    }

    /// <summary>Closes open attempts, ends open steps, releases held semaphores, stops the crew span. Called once, after the state left <c>Running</c>.</summary>
    private (int Attempts, int Steps, int Held) ReleaseEverything()
    {
        AgentAttempt[] attempts;
        AgentStep[] steps;
        JsAgent[] held;
        lock (_sync)
        {
            attempts = _attempts.ToArray();
            steps = _steps.ToArray();
            held = _held.Values.ToArray();
            foreach (var a in attempts) a.Closed = true;
            foreach (var s in steps) s.Ended = true;
            _attempts.Clear();
            _steps.Clear();
            _held.Clear();
        }
        foreach (var attempt in attempts)
        {
            _crew.EndBrokerScope(attempt.Previous);
            attempt.ctx.Dispose();
        }
        foreach (var step in steps) step.Activity?.Dispose();
        foreach (var agent in held) _crew.GetInstanceSemaphore(agent).Release();
        Activity?.Dispose();
        return (attempts.Length, steps.Length, held.Length);
    }

    private void ScheduleAbandon()
    {
        try
        {
            _abandon.CancelAfter(CancellationGrace);
        }
        catch (ObjectDisposedException)
        {
            // The registrations are disposed before the sources, so this only races a timeout firing
            // during Dispose itself — and a disposed grace source has nothing waiting on it any more.
        }
    }
}

/// <summary>One agent of a run, opened by <c>beginAgent</c>, closed by <c>endAgent</c>; opaque to JavaScript.</summary>
internal sealed class AgentStep(JsAgent agent, Activity? activity, DateTime startUtc)
{
    internal JsAgent Agent { get; } = agent;
    internal Activity? Activity { get; } = activity;
    internal DateTime StartUtc { get; } = startUtc;
    internal bool Ended;
}

/// <summary>One attempt of an agent's body, opened by <c>openAttempt</c>, closed by <c>closeAttempt</c>; JavaScript reads <c>ctx</c>.</summary>
#pragma warning disable IDE1006 // ctx is the property the JS loop reads
internal sealed class AgentAttempt(JsAgent agent, JsAgentContext ctx, BrokerScopeMemento previous, int attempt)
{
    internal JsAgent Agent { get; } = agent;
    public JsAgentContext ctx { get; } = ctx;
    internal BrokerScopeMemento Previous { get; } = previous;
    internal int Attempt { get; } = attempt;
    internal bool Closed;
}
#pragma warning restore IDE1006

/// <summary>
/// What the loop does with an <c>onError</c> decision: <c>return</c> a value (skip, fallback),
/// <c>retry</c> after <c>delayMs</c>, or <c>rethrow</c> the original error (fail, retry budget spent).
/// JavaScript reads <c>kind</c>, <c>value</c> and <c>delayMs</c>.
/// </summary>
#pragma warning disable IDE1006 // property names are the JS surface
internal sealed class ErrorDecision(string kind, JsValue value, double delayMs)
{
    public string kind { get; } = kind;
    public JsValue value { get; } = value;
    public double delayMs { get; } = delayMs;
}
#pragma warning restore IDE1006

/// <summary>The broker's current-agent attribution before an attempt opened; restored when it closes, so a nested <c>runAgent</c> hands attribution back to the body that called it.</summary>
internal readonly record struct BrokerScopeMemento(string? AgentId, CancellationToken Ct);
