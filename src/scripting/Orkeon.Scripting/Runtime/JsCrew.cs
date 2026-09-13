using Jint;
using Jint.Native;
using JsValueExt = Jint.JsValueExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Runtime crew instance produced by <see cref="JsCrewBuilder.build"/>. Exposes the
/// runtime methods documented in <c>crew.d.ts</c> (<c>run</c>, <c>runAgent</c>,
/// <c>runStream</c>, <c>add</c>, <c>remove</c>, <c>has</c>, <c>findByName</c>,
/// <c>findById</c>) and enforces mono-crew membership.
/// </summary>
/// <remarks>
/// <para>The run loop lives in JavaScript (SCR-25 T4). <see cref="run"/>, <see cref="runAgent"/> and
/// <see cref="runStream"/> are JS functions built once per crew from the run module in
/// <c>JsCrew.Run.cs</c>: they walk the agents in declaration order, take each agent's instance
/// semaphore, build its <see cref="JsAgentContext"/>, invoke its <c>body</c>, apply its
/// <c>onError</c> policy and await the crew hooks — all as promise reactions on whichever thread is
/// draining the engine. The CLR supplies synchronous helpers (bridged through
/// <see cref="JsHostError"/>, so a failure is a JavaScript throw) and three awaited tasks — the
/// cancellation, the semaphore acquisition, a retry delay — and never calls back into the engine
/// after an await. A CLR caller drives the loop only through <see cref="RunAsync"/>, a root pump on
/// an engine at rest.</para>
/// <para>Real orchestration (LLM calls, hierarchical manager, autonomous budget) is wired in via
/// <c>ICrewOrchestrationService</c> on the declarative shape (<c>globalThis.crew</c> handoff).</para>
/// </remarks>
#pragma warning disable IDE1006 // Method names match the JS surface
#pragma warning disable CS1591 // JS-interop mirror of Crew in Typings/crew.d.ts; that declaration is the contract scripts read (the CLR-facing RunAsync carries its own doc).
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1708", Justification = "The camelCase JS-surface methods (add/remove/findById…) intentionally mirror their C# PascalCase peers and collide case-only by design — this type is bound into the Jint engine where JS callers require the camelCase names.")]
public sealed partial class JsCrew
{
    private readonly Engine _engine;
    private readonly List<JsAgent> _agents;
    private readonly List<object> _tasks;
    private readonly JsAgentChannel _channel = new();
    private readonly JsMemoryScope _crewMemory = new();
    private readonly ILogger _logger;
    private readonly ILlmProvider? _llmProvider;
    private readonly IReadOnlyList<Orkeon.Domain.Tools.IBaseTool>? _builtInTools;
    private readonly Orkeon.Application.Interfaces.Security.IPermissionGate? _permissionGate;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? _deltaSink;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmUsageSink? _usageSink;

    /// <summary>Crew display name (defaults to <c>"crew"</c> when none was supplied).</summary>
    public string name { get; }

    internal string? Goal { get; }
    internal string Process { get; }
    internal JsAgent? Manager { get; }
    internal IReadOnlyDictionary<string, object?> Budget { get; }
    internal bool Verbose { get; }
    internal bool Memory { get; }
    /// <summary>Tasks captured by <c>crewBuilder().withTask(...)</c>. Exposed for the
    /// JS→orchestrator adapter; each entry is normally a <see cref="JsTask"/>.</summary>
    internal IReadOnlyList<object> Tasks => _tasks;

    internal JsCrew(Engine engine, JsCrewDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _engine = engine;
        name = definition.Name;
        Goal = definition.Goal;
        Process = definition.Process;
        Manager = definition.Manager;
        Budget = definition.Budget;
        Verbose = definition.Verbose;
        Memory = definition.Memory;
        _logger = definition.Logger ?? NullLogger.Instance;
        _llmProvider = definition.LlmProvider;
        _builtInTools = definition.BuiltInTools;
        _permissionGate = definition.PermissionGate;
        _deltaSink = definition.DeltaSink;
        _usageSink = definition.UsageSink;
        _agents = new List<JsAgent>();
        _tasks = new List<object>(definition.Tasks);

        foreach (var a in definition.Agents) Add(a);
        if (definition.Manager is not null && definition.Manager.CurrentCrew is null) Add(definition.Manager);
    }

    internal JsAgentChannel Channel => _channel;
    internal JsMemoryScope CrewMemory => _crewMemory;

    /// <summary>
    /// Builds the shared runtime environment record handed to execution contexts. The budget is the
    /// run's own (<see cref="CrewRunScope.Budget"/>): lifecycle hooks fired by add/remove and
    /// <c>runAgent</c> contexts are intentionally unbudgeted and pass <c>null</c>.
    /// </summary>
    internal JsExecutionEnvironment CreateEnvironment(JsAgent agent, AgentExecutionBudget? budget, CancellationToken ct) => new()
    {
        Engine = _engine,
        Self = agent,
        Crew = this,
        Channel = _channel,
        CrewMemory = _crewMemory,
        Logger = _logger,
        Ct = ct,
        LlmProvider = _llmProvider,
        BuiltInTools = _builtInTools,
        Budget = budget,
        PermissionGate = _permissionGate,
        DeltaSink = _deltaSink,
        UsageSink = _usageSink,
    };

    private JsEventBroker? _eventBroker;
    internal JsEventBroker EventBroker
    {
        get
        {
            if (_eventBroker is null)
            {
                _eventBroker = new JsEventBroker(_engine);
                // Plant on the engine so StateGraphBinding.Build can find the ambient CT
                // without holding a strong ref to this JsCrew.
                _engine.SetValue("__orkeon_broker", _eventBroker);
            }
            return _eventBroker;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _crewLocks =
        new(StringComparer.Ordinal);

    /// <summary>Returns the named lock for this crew, creating it lazily.</summary>
    internal SemaphoreSlim GetCrewLock(string name)
        => _crewLocks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));

    public IReadOnlyList<JsAgent> agents => _agents;

    /// <summary>The agents a run walks: taken once at its start, so a spawned agent runs in the next run and a removed one still runs in this one.</summary>
    internal JsAgent[] SnapshotAgents() => _agents.ToArray();

    // The script-facing members are bridged (JsHostError): a body past its first await calls them
    // from an event-loop job, where a raw CLR throw — an agent already in a crew, a duplicate name,
    // an agent of another crew — would skip the script's catch and finally, leave the run's promise
    // pending and erupt from the pump. The PascalCase members stay raw for C# callers (the
    // constructor's initial composition, ctx.spawn's own bridge). A lifecycle hook's synchronous
    // JavaScript throw passes through unchanged.
    public void add(JsAgent agent) => JsHostError.Guard(_engine, () => Add(agent));

    internal void Add(JsAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (agent.CurrentCrew is not null && !ReferenceEquals(agent.CurrentCrew, this))
            throw new AgentAlreadyInCrewException(agent.name, agent.CurrentCrew.name);
        if (_agents.Any(a => string.Equals(a.name, agent.name, StringComparison.Ordinal) && !ReferenceEquals(a, agent)))
            throw new DuplicateAgentNameException(agent.name, name);
        if (!_agents.Contains(agent))
        {
            _agents.Add(agent);
            agent.CurrentCrew = this;
            InvokeAgentLifecycleHook(agent, agent.Builder.OnAgentStartHandler, "onAgentStart");
        }
    }

    public void remove(JsAgent agent) => JsHostError.Guard(_engine, () => Remove(agent));

    internal void Remove(JsAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!_agents.Remove(agent))
            throw new AgentNotInThisCrewException(agent.name, name);
        // Auto-cleanup: drop every topic subscription registered while this agent was
        // executing. Queues are crew-scoped and intentionally kept.
        _eventBroker?.DetachAgent(agent.id);
        InvokeAgentLifecycleHook(agent, agent.Builder.OnAgentStopHandler, "onAgentStop");
        if (ReferenceEquals(agent.CurrentCrew, this))
            agent.CurrentCrew = null;
    }

    /// <summary>
    /// Chapter 05: the hook is "invoked synchronously". What it returns is left to settle under
    /// whichever pump is active — never drained: from <c>ctx.spawn</c> inside a body this runs inside
    /// an event-loop job, where a drain cannot pump (SCR-25 §2.3). A rejection is observed and logged;
    /// a synchronous call site has nowhere else to put it. Called by <see cref="Add"/> (the
    /// constructor's initial composition included) and <see cref="Remove"/>, on the engine thread —
    /// inside <c>build()</c>'s prefix, inside a body's job, or from a C# caller with the engine at
    /// rest — so the <c>Invoke</c> calls are legal under the rule.
    /// </summary>
    /// <remarks>
    /// A hook that awaits real host work in a script that never pumps again (a purely synchronous
    /// script with no run, or <see cref="Remove"/> called from C# with the engine at rest and no
    /// later pump) has its tail run at the next pump on that engine, or never; its rejection is still
    /// logged when it settles.
    /// </remarks>
    private void InvokeAgentLifecycleHook(JsAgent agent, JsValue? hook, string hookName)
    {
        if (hook is null || hook.IsUndefined() || hook.IsNull()) return;
        var ctx = new JsExecutionContext(CreateEnvironment(agent, budget: null, CancellationToken.None));
        var raw = _engine.Invoke(hook, [ctx]);
        if (!raw.IsPromise()) return;
        Action<JsValue> onRejected = reason => JsHostError.Guard(_engine,
            () => LogLifecycleHookRejected(_logger, hookName, agent.name, name, DisplayMessage(reason)));
        _engine.Invoke(Observe, [raw, onRejected]);
    }

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Lifecycle hook {Hook} of agent '{Agent}' in crew '{Crew}' rejected: {Reason}")]
    private static partial void LogLifecycleHookRejected(ILogger logger, string hook, string agent, string crew, string reason);

    internal JsValue? _onCrewStart;
    internal JsValue? _onCrewComplete;
    internal JsValue? _onCrewError;

    public bool has(JsAgent agent) => _agents.Contains(agent);

    public JsAgent? findByName(string name)
        => _agents.FirstOrDefault(a => string.Equals(a.name, name, StringComparison.Ordinal));

    public JsAgent? findById(string id)
        => _agents.FirstOrDefault(a => string.Equals(a.id, id, StringComparison.Ordinal));

    public IReadOnlyList<JsAgent> findByRole(string role)
        => _agents.Where(a => string.Equals(a.Domain.Role.Value, role, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// The CLR entry point (ScriptHost's <c>globalThis.crew</c> handoff, hosts, tests): a root pump. The
    /// engine must be at rest — the whole run, the synchronous prefix of the JS run function and every
    /// event-loop job, is driven from one pool thread, and the per-engine gate serializes it against any
    /// other root pump on this engine (another <see cref="RunAsync"/>, a <see cref="JsTool.CallAsync"/>,
    /// a <see cref="ScriptHost"/> evaluation). From a script, call <c>crew.run()</c>; from a CLR delegate
    /// the script invoked, this throws (same thread) or deadlocks on the gate (another thread) — never
    /// call it from inside a body.
    /// </summary>
    /// <remarks>
    /// <para>What a caller receives, in precedence order: an <see cref="OperationCanceledException"/> (exact
    /// type) whenever the run's linked token — <paramref name="externalCt"/>, <c>options.signal</c>,
    /// <c>options.timeout</c> — is cancelled, whatever the JavaScript rejected with; the typed host
    /// exception a bridged or wrapped rejection carries (<see cref="StateMutationOutsideWithException"/>,
    /// <see cref="RecursiveAgentInvocationException"/>, <see cref="BudgetExhaustedException"/>);
    /// otherwise Jint's <c>PromiseRejectedException</c> carrying the rejected value. A bad
    /// <c>timeout</c> option is a <see cref="FormatException"/>, raised before the loop starts.</para>
    /// <para>A cancelled run rejects promptly even when a body sits in a host await that ignores
    /// <c>ctx.signal</c>: the loop races every body await against the cancellation, closes the attempt
    /// and hands the instance semaphore back while that body is still executing. Its context is
    /// disposed, so its later failures are its own unobserved rejection, but the body's remaining
    /// continuations still run under whichever drain is active — during the next run of the same
    /// agent, if one starts at once. It is the one cancellation case where per-instance serialization
    /// does not hold; a body that observes <c>ctx.signal</c> never gets there.</para>
    /// </remarks>
    public async Task<JsCrewResult> RunAsync(JsValue? options, CancellationToken externalCt)
    {
        if (JsEngineGate.IsDrainingOnThisThread(_engine))
        {
            throw new InvalidOperationException(
                $"JsCrew.RunAsync is a root pump and needs the engine at rest, but crew '{name}' was asked to run from " +
                "inside this engine's own event loop (a CLR callback the script invoked). Call crew.run() from the script instead.");
        }
        externalCt.ThrowIfCancellationRequested();
        var gate = JsEngineGate.For(_engine);
        try
        {
            await gate.WaitAsync(externalCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The exact type, not the TaskCanceledException the wait raises: callers assert on it.
            throw new OperationCanceledException(externalCt);
        }
        try
        {
            // No token on Task.Run: a cancelled one would surface as TaskCanceledException, and every
            // cancellation path of this run is normalised inside Pump.
            return await Task.Run(() => Pump(options, externalCt)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Synchronous by design: one pool thread parses the options, invokes the JS run function and drains
    /// the engine until its promise settles or the run is abandoned. Draining on the run token itself
    /// would abandon the drain at the first idle wait after cancellation, before any JS <c>finally</c>
    /// or <c>onCrewError</c> ran; draining unbounded would hang on an unwind nobody settles. So the
    /// drain token is the run token plus <see cref="CrewRunScope.CancellationGrace"/>.
    /// </summary>
    private JsCrewResult Pump(JsValue? options, CancellationToken externalCt)
    {
        using var draining = JsEngineGate.MarkDraining(_engine);
        var (signal, timeout) = CrewRunOptions.From(options);
        using var scope = new CrewRunScope(this, CrewRunKind.Run, signal, timeout, ownedByHost: true, externalCt);
        try
        {
            var promise = _engine.Invoke(RunFromHost, [scope]);
            var settled = JsValueExt.UnwrapIfPromise(promise, scope.AbandonToken);
            return (JsCrewResult)settled.ToObject()!;
        }
        catch (Exception ex) when (scope.Token.IsCancellationRequested)
        {
            // Whatever the JS rejected with — a bridged OperationCanceledException, Jint's
            // ExecutionCanceledException for a cancelled task, the drain's own cancellation at the
            // grace — the caller sees the cancellation.
            throw new OperationCanceledException("The crew run was cancelled.", ex, scope.Token);
        }
        catch (Exception ex) when (TryUnwrapTypedHostException(ex, out var typed))
        {
            throw typed;
        }
        finally
        {
            // A normal run already ended its scope in JS; this releases leftovers only when the pump
            // gave up first (grace expired, sandbox TimeoutException / MemoryLimitExceededException, a
            // raw throw from a non-bridged binding).
            scope.Abandon();
        }
    }

    private bool _warnedAboutDeclarativeOnly;

    /// <summary>
    /// Says out loud what this engine does not read, once per crew instance.
    /// </summary>
    /// <remarks>
    /// The mirror of <c>JsCrewConfigurationAdapter.CollectIgnoredFeatures</c>: the two shapes
    /// each drop the other's half, and this is the half dropped here. A crew that declares
    /// tasks and ends with <c>await crew.run()</c> runs its agents in declaration order and
    /// never looks at the tasks — the most common way to get a plausible run that did none
    /// of the work the script describes. Once per instance, not once per run: the condition
    /// is fixed at build time, so a crew run in a loop would repeat an unchanging fact.
    /// </remarks>
    internal void WarnOnceAboutDeclarativeOnlyDeclarations()
    {
        if (_warnedAboutDeclarativeOnly) return;
        _warnedAboutDeclarativeOnly = true;

        if (_tasks.Count > 0)
        {
            LogProceduralShapeIgnores(_logger,
                $"crew '{name}' declares {_tasks.Count} task(s) that this run will not read. "
                + "The procedural engine runs agent .body() in declaration order; tasks, their "
                + "withContext DAG and their deliverables belong to the declarative shape. "
                + "End the script with `globalThis.crew = crew` instead of `await crew.run()` "
                + "to execute the tasks.");
        }

        if (Manager is not null)
        {
            LogProceduralShapeIgnores(_logger,
                $"crew '{name}' declares a manager agent, which this run will not use: "
                + "delegation is orchestrated on the declarative shape.");
        }

        if (!string.Equals(Process, "sequential", StringComparison.Ordinal))
        {
            LogProceduralShapeIgnores(_logger,
                $"crew '{name}' declares process(\"{Process}\"), which reaches only a telemetry "
                + "tag here: the procedural engine always runs agents in declaration order.");
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Procedural crew script: {Detail}")]
    private static partial void LogProceduralShapeIgnores(ILogger logger, string detail);

    internal void LogRunAbandoned(int openAttempts, int openSteps, int heldSemaphores)
        => LogRunAbandoned(_logger, name, openAttempts, openSteps, heldSemaphores);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Crew '{Crew}' run abandoned by its host before its JavaScript loop ended; released from the host: {OpenAttempts} open attempt(s), {OpenSteps} open agent step(s), {HeldSemaphores} held semaphore(s).")]
    private static partial void LogRunAbandoned(ILogger logger, string crew, int openAttempts, int openSteps, int heldSemaphores);

    /// <summary>
    /// Walks <paramref name="ex"/> looking for a typed CLR exception thrown by a host
    /// delegate inside JS (e.g. the state-mutation set-trap, bridged by
    /// <see cref="JsHostError"/>). Jint surfaces these as a <c>JavaScriptException</c> or a
    /// <c>PromiseRejectedException</c> whose Error carries the CLR exception; this helper
    /// lets the original CLR type surface to <c>Assert.ThrowsAsync&lt;T&gt;</c>.
    /// </summary>
    private static bool TryUnwrapTypedHostException(Exception ex, out Exception typed)
    {
        var visited = new HashSet<Exception>();
        for (Exception? cur = ex; cur is not null && visited.Add(cur); cur = NextInner(cur))
        {
            if (cur is Exceptions.StateMutationOutsideWithException sm) { typed = sm; return true; }
            if (cur is Exceptions.RecursiveAgentInvocationException ra) { typed = ra; return true; }
            if (cur is Orkeon.Domain.Autonomous.BudgetExhaustedException be) { typed = be; return true; }
        }
        typed = null!;
        return false;
    }

    /// <summary>
    /// Next step of the unwrap walk. A <see cref="Jint.Runtime.PromiseRejectedException"/>
    /// carries the host exception as its *rejected value* (not <c>InnerException</c>);
    /// an <see cref="AggregateException"/> hides it in its first inner exception.
    /// </summary>
    private static Exception? NextInner(Exception cur)
    {
        // A promise rejected with a bridged Error (JsHostError.Wrap) or with a wrapped CLR
        // exception (a faulted Task) carries the host exception on the rejected value; a
        // JavaScriptException thrown through a synchronous Invoke carries it on its Error.
        if (cur is Jint.Runtime.PromiseRejectedException pre &&
            JsHostError.Unwrap(pre.RejectedValue) is { } rejected)
            return rejected;
        if (cur is Jint.Runtime.JavaScriptException jse &&
            JsHostError.Unwrap(jse.Error) is { } thrown)
            return thrown;
        if (cur is AggregateException agg && agg.InnerExceptions.Count >= 1)
            return agg.InnerExceptions[0];
        return cur.InnerException;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _instanceSemaphores =
        new(StringComparer.Ordinal);

    internal SemaphoreSlim GetInstanceSemaphore(JsAgent agent)
        => _instanceSemaphores.GetOrAdd(agent.id, _ => new SemaphoreSlim(1, 1));

    // The open attempts of this crew in opening order; the last one is what the broker attributes to.
    // Guarded by its own lock: attempts open on the draining thread and close there or, when a host
    // abandons its run, on the host thread.
    private readonly List<BrokerAttribution> _attributions = new();
    private readonly Lock _attributionSync = new();

    /// <summary>
    /// Attributes what happens next to <paramref name="agent"/> — <see cref="JsEventTopic.subscribe"/>
    /// records the subscriber, <c>stateGraph.run()</c> and a topic handler's <c>ev.lock</c> pick up
    /// the ambient token — for as long as the attempt is open, and registers the attempt on the engine
    /// (<see cref="JsEngineAttempts"/>), where a run opened from the body finds its parent whichever
    /// crew it belongs to. The entries form the set of open attempts, not a stack of previous values:
    /// two runs of one crew interleaved on one event loop close their attempts in any order, and
    /// restoring "what was current when this one opened" would hand a closed attempt's attribution
    /// back to an idle crew. When an attempt closes, the attribution is the most recently opened
    /// attempt still open, or none — so a nested <c>runAgent</c> hands it back to the body that called
    /// it, and an idle crew attributes nothing. The context constructor has already created the broker.
    /// </summary>
    internal BrokerAttribution BeginBrokerScope(JsAgent agent, CrewRunScope scope)
    {
        var broker = EventBroker;
        var entry = new BrokerAttribution(agent.id, scope);
        lock (_attributionSync)
        {
            _attributions.Add(entry);
            broker.CurrentAgentId = entry.AgentId;
            broker.CurrentCt = entry.Ct;
        }
        JsEngineAttempts.Open(_engine, entry);
        return entry;
    }

    internal void EndBrokerScope(BrokerAttribution entry)
    {
        JsEngineAttempts.Close(_engine, entry);
        var broker = _eventBroker;
        if (broker is null) return;
        lock (_attributionSync)
        {
            _attributions.Remove(entry);
            var current = _attributions.Count == 0 ? null : _attributions[^1];
            broker.CurrentAgentId = current?.AgentId;
            broker.CurrentCt = current?.Ct ?? CancellationToken.None;
        }
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
