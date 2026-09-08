using System.Buffers;
using Jint;
using Jint.Native;
using JsValueExt = Jint.JsValueExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.ErrorPolicy;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Telemetry;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Runtime crew instance produced by <see cref="JsCrewBuilder.build"/>. Exposes the
/// runtime methods documented in <c>crew.d.ts</c> (<c>run</c>, <c>runAgent</c>,
/// <c>runStream</c>, <c>add</c>, <c>remove</c>, <c>has</c>, <c>findByName</c>,
/// <c>findById</c>) and enforces mono-crew membership.
/// </summary>
/// <remarks>
/// SCR-04 minimal: <c>run</c> walks the agents in declaration order and invokes each
/// agent's <c>body</c> (when supplied), capturing string-coerced outputs. Real
/// orchestration (LLM calls, hierarchical manager, autonomous budget) is wired in via
/// <c>ICrewOrchestrationService</c> in later tasks.
/// </remarks>
#pragma warning disable IDE1006 // Method names match the JS surface
#pragma warning disable CS1591 // JS-interop mirror of Crew in Typings/crew.d.ts; that declaration is the contract scripts read (the CLR-facing RunAsync carries its own doc).
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1708", Justification = "The camelCase JS-surface methods (add/remove/findById…) intentionally mirror their C# PascalCase peers and collide case-only by design — this type is bound into the Jint engine where JS callers require the camelCase names.")]
public sealed partial class JsCrew
{
    private static readonly SearchValues<char> HostExceptionDelimiters = SearchValues.Create(" \n\r\t");

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
    /// Typed budget for the crew run currently in flight (see <see cref="RunAsync"/>).
    /// Null outside a run — lifecycle hooks fired by add/remove and direct
    /// <c>runAgent</c> calls are intentionally unbudgeted.
    /// </summary>
    private Orkeon.Domain.Autonomous.AgentExecutionBudget? _currentRunBudget;

    /// <summary>Builds the shared runtime environment record handed to execution contexts.</summary>
    private JsExecutionEnvironment CreateEnvironment(JsAgent agent, CancellationToken ct) => new()
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
        Budget = _currentRunBudget,
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

    public void add(JsAgent agent) => Add(agent);

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
            InvokeAgentLifecycleHook(agent, agent.Builder.OnAgentStartHandler);
        }
    }

    public void remove(JsAgent agent) => Remove(agent);

    internal void Remove(JsAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!_agents.Remove(agent))
            throw new AgentNotInThisCrewException(agent.name, name);
        // Auto-cleanup: drop every topic subscription registered while this agent was
        // executing. Queues are crew-scoped and intentionally kept.
        _eventBroker?.DetachAgent(agent.id);
        InvokeAgentLifecycleHook(agent, agent.Builder.OnAgentStopHandler);
        if (ReferenceEquals(agent.CurrentCrew, this))
            agent.CurrentCrew = null;
    }

    private void InvokeAgentLifecycleHook(JsAgent agent, JsValue? hook)
    {
        if (hook is null || hook.IsUndefined() || hook.IsNull()) return;
        var ctx = new JsExecutionContext(CreateEnvironment(agent, CancellationToken.None));
        var result = _engine.Invoke(hook, [ctx]);
        if (result.IsPromise()) result.UnwrapIfPromise();
    }

    internal JsValue? _onCrewStart;
    internal JsValue? _onCrewComplete;
    internal JsValue? _onCrewError;

    private void InvokeCrewHook(JsValue? hook, JsCrewResult? result, Exception? error, CancellationToken ct)
    {
        if (hook is null || hook.IsUndefined() || hook.IsNull()) return;
        var anchor = _agents.FirstOrDefault();
        if (anchor is null) return;
        var ctx = new JsExecutionContext(CreateEnvironment(anchor, ct));
        // Peel Jint promise/aggregate wrappers so the JS hook receives the
        // actionable inner message instead of "Promise was rejected with value
        // System.AggregateException ...". See JsExceptionUnwrap.
        var displayedError = error is null ? null : JsExceptionUnwrap.UnwrapToInnermost(error).Message;
        object[] args;
        if (error is not null)
            args = [ctx, displayedError!];
        else if (result is not null)
            args = [ctx, result];
        else
            args = [ctx];
        var raw = _engine.Invoke(hook, args);
        if (raw.IsPromise()) raw.UnwrapIfPromise(ct);
    }

    public bool has(JsAgent agent) => _agents.Contains(agent);

    public JsAgent? findByName(string name)
        => _agents.FirstOrDefault(a => string.Equals(a.name, name, StringComparison.Ordinal));

    public JsAgent? findById(string id)
        => _agents.FirstOrDefault(a => string.Equals(a.id, id, StringComparison.Ordinal));

    public IReadOnlyList<JsAgent> findByRole(string role)
        => _agents.Where(a => string.Equals(a.Domain.Role.Value, role, StringComparison.Ordinal)).ToList();

    public Task<JsCrewResult> run() => RunAsync(null, CancellationToken.None);

    public Task<JsCrewResult> run(JsValue? options) => RunAsync(options, CancellationToken.None);

    /// <summary>
    /// Internal entry point used by tests and host integrations that already have a
    /// <see cref="CancellationToken"/>.
    /// </summary>
    public async Task<JsCrewResult> RunAsync(JsValue? options, CancellationToken externalCt)
    {
        var (signal, timeout) = CrewRunOptions.From(options);
        using var timeoutCts = timeout is null ? null : new CancellationTokenSource(timeout.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            externalCt, signal ?? CancellationToken.None, timeoutCts?.Token ?? CancellationToken.None);

        using var crewActivity = ScriptingActivitySource.Instance.StartActivity(ScriptingActivitySource.CrewRunSpan);
        crewActivity?.SetTag("crew.name", name);
        crewActivity?.SetTag("crew.process", Process);

        // One typed budget instance per crew run, shared by every agent of the run  —
        // created here (not at build time) so the wall-time clock starts with the run.
        _currentRunBudget = JsBudgetBridge.FromSpec(Budget);

        WarnOnceAboutDeclarativeOnlyDeclarations();

        InvokeCrewHook(_onCrewStart, result: null, error: null, linked.Token);

        var taskResults = new List<JsTaskResult>(_agents.Count);
        var aggregate = string.Empty;

        // Snapshot the agent list so spawned agents (added via ctx.spawn) don't break
        // iteration. Spawned agents become available to subsequent crew.run() calls.
        var snapshot = _agents.ToList();
        try
        {
            foreach (var agent in snapshot)
            {
                linked.Token.ThrowIfCancellationRequested();
                var start = DateTime.UtcNow;
                object? output = null;
                if (agent.Builder.BodyFunction is not null && !agent.Builder.BodyFunction.IsUndefined())
                {
                    using var agentActivity = ScriptingActivitySource.Instance.StartActivity(ScriptingActivitySource.AgentRunSpan);
                    agentActivity?.SetTag("agent.name", agent.name);
                    agentActivity?.SetTag("agent.id", agent.id);
                    output = await RunAgentBodyWithErrorPolicyAsync(agent, linked.Token).ConfigureAwait(false);
                }
                var duration = (DateTime.UtcNow - start).TotalMilliseconds;
                taskResults.Add(new JsTaskResult(agent.name, output, duration));
                if (output is not null) aggregate = output.ToString() ?? aggregate;
            }

            var crewResult = new JsCrewResult(aggregate, new Dictionary<string, object?>(), taskResults);
            InvokeCrewHook(_onCrewComplete, crewResult, error: null, linked.Token);
            return crewResult;
        }
        catch (Exception ex)
        {
            InvokeCrewHook(_onCrewError, result: null, error: ex, linked.Token);
            throw;
        }
        finally
        {
            _currentRunBudget = null;
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
    private void WarnOnceAboutDeclarativeOnlyDeclarations()
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

    public Task<object?> runAgent(JsAgent agent, JsValue? input)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!has(agent))
            throw new AgentNotInThisCrewException(agent.name, name);
        return RunAgentCoreAsync(agent, input);
    }

    private async Task<object?> RunAgentCoreAsync(JsAgent agent, JsValue? input)
    {
        if (agent.Builder.BodyFunction is null || agent.Builder.BodyFunction.IsUndefined()) return null;
        var result = _engine.Invoke(agent.Builder.BodyFunction, [input ?? JsValue.Undefined, JsValue.Undefined]);
        return result.IsPromise() ? await UnwrapPromise(result, CancellationToken.None).ConfigureAwait(false) : result.ToObject();
    }

    /// <summary>
    /// Skeleton stream emitting an <c>agent.start</c>/<c>agent.stop</c> pair per agent.
    /// Full streaming semantics land in SCR-17 alongside <c>stateGraph.runStream</c>.
    /// </summary>
    public async IAsyncEnumerable<object> runStream(JsValue? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var agent in _agents)
        {
            ct.ThrowIfCancellationRequested();
            yield return new { type = "agent.start", payload = new { name = agent.name }, at = DateTime.UtcNow.Ticks };
            object? output = null;
            if (agent.Builder.BodyFunction is not null && !agent.Builder.BodyFunction.IsUndefined())
            {
                var result = _engine.Invoke(agent.Builder.BodyFunction, [JsValue.Undefined, JsValue.Undefined]);
                output = result.IsPromise() ? await UnwrapPromise(result, ct).ConfigureAwait(false) : result.ToObject();
            }
            yield return new { type = "agent.stop", payload = new { name = agent.name, output }, at = DateTime.UtcNow.Ticks };
        }
    }

    /// <summary>
    /// Body promises must survive multi-second host awaits (LLM HTTP round-trips, slow
    /// tools, …). Jint's <c>UnwrapIfPromiseAsync</c> bakes in a 10 s ceiling that we
    /// can't raise from outside the engine. The synchronous <c>UnwrapIfPromise(TimeSpan)</c>
    /// overload accepts a custom timeout — 30 minutes exceeds any realistic single-agent
    /// body. We dispatch to a background thread via <c>Task.Run</c> so the caller's
    /// await semantics are preserved.
    /// </summary>
    private static readonly TimeSpan BodyPromiseTimeout = TimeSpan.FromMinutes(30);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1849", Justification = "Blocking here is the point: this thread already owns the engine (crew.run() was called from the script), and UnwrapIfPromise drains the engine's continuations while it waits. UnwrapIfPromiseAsync would resume the pump on a pool thread, putting a second thread inside a Jint engine that is neither thread-safe nor re-entrant — measured: 1 full-suite run in 6 failed that way, and forcing a dedicated thread made it 4 in 4. It also bakes in a 10s ceiling this call must not have.")]
    private static Task<object?> UnwrapPromise(JsValue promise, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // Inline, on the calling thread — NOT on a pool thread. `crew.run()` is invoked
            // from the script, so this thread is already inside `engine.Evaluate`; settling the
            // body's promise means re-entering that same engine, and a Jint engine is neither
            // thread-safe nor re-entrant. `UnwrapIfPromise` drains the engine's continuations
            // while it waits, which is precisely the pump this thread owes the engine — moving
            // it onto another thread put two threads inside one engine and produced everything
            // but the cause (a body resuming with its parameters unbound, a result that never
            // settles).
            var settled = JsValueExt.UnwrapIfPromise(promise, BodyPromiseTimeout);
            return Task.FromResult(settled.ToObject());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Jint.Runtime.PromiseRejectedException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex) when (TryUnwrapTypedHostException(ex, out var typed))
        {
            throw typed;
        }
    }

    /// <summary>
    /// Walks <paramref name="ex"/> looking for a typed CLR exception thrown by a host
    /// delegate inside JS (e.g. the state-mutation set-trap). Jint wraps these in
    /// <c>JavaScriptException</c> / <c>PromiseRejectedException</c>; this helper lets
    /// the original CLR type surface to <c>Assert.ThrowsAsync&lt;T&gt;</c>.
    /// </summary>
    private static bool TryUnwrapTypedHostException(Exception ex, out Exception typed)
    {
        var visited = new HashSet<Exception>();
        for (Exception? cur = ex; cur is not null && visited.Add(cur); cur = NextInner(cur))
        {
            if (cur is Exceptions.StateMutationOutsideWithException sm) { typed = sm; return true; }
            if (cur is Exceptions.RecursiveAgentInvocationException ra) { typed = ra; return true; }
            if (cur is Orkeon.Domain.Autonomous.BudgetExhaustedException be) { typed = be; return true; }
            // The set-trap originally threw a JS Error whose message embedded the typed
            // marker; reconstruct the typed exception when that's the only signal left.
            var msg = cur.Message ?? string.Empty;
            const string marker = "StateMutationOutsideWithError:";
            var idx = msg.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var prop = msg[(idx + marker.Length)..].Trim();
                var end = prop.AsSpan().IndexOfAny(HostExceptionDelimiters);
                if (end > 0) prop = prop[..end];
                typed = new Exceptions.StateMutationOutsideWithException(prop);
                return true;
            }
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
        if (cur is Jint.Runtime.PromiseRejectedException pre &&
            pre.RejectedValue is not null &&
            pre.RejectedValue.ToObject() is Exception rejected)
            return rejected;
        if (cur is AggregateException agg && agg.InnerExceptions.Count >= 1)
            return agg.InnerExceptions[0];
        return cur.InnerException;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _instanceSemaphores =
        new(StringComparer.Ordinal);

    private SemaphoreSlim GetInstanceSemaphore(JsAgent agent)
        => _instanceSemaphores.GetOrAdd(agent.id, _ => new SemaphoreSlim(1, 1));

    private async Task<object?> RunAgentBodyWithErrorPolicyAsync(JsAgent agent, CancellationToken ct)
    {
        var sem = GetInstanceSemaphore(agent);
        var attempt = 1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var initialState = ResolveInitialState(agent);
                using var ctx = new JsAgentContext(CreateEnvironment(agent, ct), initialState);
                BeginBrokerScope(agent, ct);
                try
                {
                    var result = _engine.Invoke(agent.Builder.BodyFunction!, [JsValue.Undefined, ctx]);
                    return result.IsPromise() ? await UnwrapPromise(result, ct).ConfigureAwait(false) : result.ToObject();
                }
                catch (Exception ex) when (agent.Builder.OnErrorHandler is not null && !agent.Builder.OnErrorHandler.IsUndefined())
                {
                    var (outcome, value) = await ApplyErrorPolicyAsync(agent, ex, attempt, ctx, ct).ConfigureAwait(false);
                    switch (outcome)
                    {
                        case ErrorPolicyOutcome.Return: return value;
                        case ErrorPolicyOutcome.Retry: attempt++; continue;
                        default: throw; // Fail / retry budget exhausted: preserve original exception
                    }
                }
            }
            finally
            {
                EndBrokerScope();
                sem.Release();
            }
        }
    }

    // Track who is currently executing so JsEventTopic.subscribe can attribute
    // the new subscription to this agent (used by Remove auto-cleanup) and so
    // stateGraph.run() can pick up the ambient CT.
    private void BeginBrokerScope(JsAgent agent, CancellationToken ct)
    {
        var broker = _eventBroker;
        if (broker is null) return;
        broker.CurrentAgentId = agent.id;
        broker.CurrentCt = ct;
    }

    private void EndBrokerScope()
    {
        var broker = _eventBroker;
        if (broker is null) return;
        broker.CurrentAgentId = null;
        broker.CurrentCt = CancellationToken.None;
    }

    private enum ErrorPolicyOutcome { Return, Retry, Rethrow }

    /// <summary>
    /// Applies the agent's onError policy. Returns <see cref="ErrorPolicyOutcome.Return"/>
    /// with the value to surface, <see cref="ErrorPolicyOutcome.Retry"/> to re-run the body,
    /// or <see cref="ErrorPolicyOutcome.Rethrow"/> when the policy is Fail or the retry
    /// budget is exhausted (the caller rethrows the original exception to preserve its type).
    /// </summary>
    private async Task<(ErrorPolicyOutcome Outcome, object? Value)> ApplyErrorPolicyAsync(
        JsAgent agent, Exception ex, int attempt, JsAgentContext ctx, CancellationToken ct)
    {
        var action = InvokeOnError(agent, ex, attempt, ctx);
        switch (action.kind)
        {
            case JsErrorActionKind.Skip:
                return (ErrorPolicyOutcome.Return, null);
            case JsErrorActionKind.Fallback:
                return (ErrorPolicyOutcome.Return, action.fallbackValue?.ToObject());
            case JsErrorActionKind.Retry:
                if (action.max is int m && attempt >= m)
                    return (ErrorPolicyOutcome.Rethrow, null);
                if (action.delay is TimeSpan d && d > TimeSpan.Zero)
                    await Task.Delay(d, ct).ConfigureAwait(false);
                return (ErrorPolicyOutcome.Retry, null);
            default:
                return (ErrorPolicyOutcome.Rethrow, null);
        }
    }

    private JsErrorAction InvokeOnError(JsAgent agent, Exception ex, int attempt, JsAgentContext ctx)
    {
        var inner = ex is Jint.Runtime.JavaScriptException jse
            ? new InvalidOperationException(jse.Message, jse)
            : ex;
        var code = ErrorCodeMapper.MapToCode(inner);
        var errCtx = new JsErrorContext(code, inner.Message, inner, attempt,
            new JsAgentRef(agent.id, agent.name));
        var raw = _engine.Invoke(agent.Builder.OnErrorHandler!, [errCtx, ctx]);
        var unwrapped = raw.IsPromise() ? raw.UnwrapIfPromise() : raw;
        var clr = unwrapped.ToObject();
        return clr is JsErrorAction action
            ? action
            : new JsErrorAction(JsErrorActionKind.Fail);
    }

    private JsValue ResolveInitialState(JsAgent agent)
    {
        var factory = agent.Builder.StateFactory;
        if (factory is null || factory.IsUndefined() || factory.IsNull())
            return JsValue.Undefined;
        // A non-callable factory is a plain seed value — use it directly. Only invoke
        // when it is actually a function, so a genuine error thrown by a user-supplied
        // factory propagates instead of being silently swallowed (was: broad catch).
        if (factory is not Jint.Native.Function.Function)
            return factory;
        var result = _engine.Invoke(factory, Array.Empty<object>());
        return result.IsPromise() ? result.UnwrapIfPromise() : result;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
