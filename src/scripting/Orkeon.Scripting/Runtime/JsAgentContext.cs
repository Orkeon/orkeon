using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Surface exposed to JS as <c>ctx</c> in agent <c>body</c> callbacks. Extends
/// <see cref="JsExecutionContext"/> with state, agent-scoped memory, agent-scoped
/// locks and dynamic spawn. Hooks (<c>onAgentStart</c>, <c>onAgentStop</c>) receive
/// the parent <see cref="JsExecutionContext"/> instead.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of AgentContext in Typings/context.d.ts; that declaration is the contract scripts read.
public sealed class JsAgentContext : JsExecutionContext, IDisposable
{
    // The loop that calls back into JS lives in JS (SCR-25): the CLR only hands it
    // synchronous helpers and one Task (the mutex acquisition, settled on the engine's own
    // loop). The transform therefore runs as a promise reaction on whichever thread drains
    // the engine, never on the pool thread that won a contended mutex, and `commit` rebuilds
    // the proxy from that same thread. The former CLR closure awaited the mutex, then invoked
    // the transform and rebuilt the proxy from its continuation — a second thread inside the
    // engine as soon as two `with` calls overlapped (NullReferenceException or a
    // PromiseTimeout under `Promise.all`).
    private const string StateWithFactorySource = """
        (acquire, release, current, commit, state) => async function stateWith(transform) {
            await acquire();
            try {
                const next = await transform(current());
                commit(next);
                return state();
            } finally { release(); }
        }
        """;

    // `with` is the trampoline above; any other set is refused through the host error
    // bridge, so the script's catch/finally run and the CLR side still recovers the typed
    // StateMutationOutsideWithException (JsCrew.TryUnwrapTypedHostException).
    private const string StateProxyFactorySource = """
        (current, withFn, reject) => new Proxy(current, {
            get(target, prop) {
                if (prop === 'with') return withFn;
                return target[prop];
            },
            set(target, prop, value) {
                reject(String(prop));
                return true;
            }
        })
        """;

    private readonly Engine _engineRef;
    private readonly JsAgent _self;
    private readonly JsCrew _crewRef;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SemaphoreSlim _stateMutex = new(1, 1);
    private JsValue _stateRaw;
    private JsValue? _stateWithJs;
    private JsValue? _stateProxyFactory;
    private Action<string>? _rejectStateMutation;

    public JsValue state { get; private set; }

    internal JsAgentContext(JsExecutionEnvironment environment, JsValue initialState)
        : base(environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _engineRef = environment.Engine;
        _self = environment.Self;
        _crewRef = environment.Crew;
        _stateRaw = initialState ?? JsValue.Undefined;
        memory.agent = new JsMemoryScope();
        state = BuildStateProxy(_stateRaw);
    }

    /// <summary>
    /// The one mutator of <see cref="state"/>: a JS async function (built once per context)
    /// that serialises transforms on the agent's state mutex and resolves to the new state
    /// proxy. Reached as <c>ctx.state.with(...)</c> and as <c>ctx.stateWith(...)</c>.
    /// </summary>
    public JsValue stateWith => _stateWithJs ??= BuildStateWithFunction();

    private JsValue BuildStateWithFunction()
    {
        Func<Task<JsValue>> acquire = async () =>
        {
            await _stateMutex.WaitAsync(signal).ConfigureAwait(false);
            return JsValue.Undefined;
        };
        // Every synchronous helper that can throw is bridged (JsHostError): a CLR exception
        // that unwinds through the script skips its catch and finally and leaves the promise
        // pending, so the mutex would stay held. A state the proxy cannot wrap (a primitive)
        // is already a JS throw — the TypeError of the proxy factory — and passes through.
        Action release = () => JsHostError.Guard(_engineRef, () => _stateMutex.Release());
        Func<JsValue> current = () => _stateRaw;
        Action<JsValue> commit = next => JsHostError.Guard(_engineRef, () =>
        {
            var proxy = BuildStateProxy(next);
            _stateRaw = next;
            state = proxy;
        });
        Func<JsValue> stateAccessor = () => state;
        var factory = _engineRef.Evaluate(StateWithFactorySource);
        return _engineRef.Invoke(factory, [acquire, release, current, commit, stateAccessor]);
    }

    private JsValue? _lockJs;
    // Implemented in JS rather than C# to avoid nesting `UnwrapIfPromise` inside an
    // engine callback: Jint's `EventLoop.RunAvailableContinuations` is guarded by an
    // `_isProcessing` CAS, so a re-entrant unwrap from a C# delegate that is itself
    // running as part of an outer pump can't drain microtasks and dead-locks until
    // `PromiseTimeout` expires. The JS wrapper only awaits real Tasks (acquire) and
    // chains directly on the user callback's promise via the outer pump.
    public JsValue @lock => _lockJs ??= BuildLockFunction();

    private JsValue BuildLockFunction()
    {
        Func<string, Task<JsValue>> acquire = async name =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var sem = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(signal).ConfigureAwait(false);
            return JsValue.Undefined;
        };
        Action<string> release = name =>
        {
            if (_locks.TryGetValue(name, out var sem)) sem.Release();
        };
        var factory = _engineRef.Evaluate("""
            (acquire, release) => async function lock(name, fn) {
                await acquire(name);
                try { return await fn(); }
                finally { release(name); }
            }
            """);
        return _engineRef.Invoke(factory, [acquire, release]);
    }

    public Func<JsAgentBuilder, JsAgent> spawn => builder =>
    {
        ArgumentNullException.ThrowIfNull(builder);
        var spawned = builder.build();
        if (string.Equals(spawned.name, _self.name, StringComparison.Ordinal))
            throw new RecursiveAgentInvocationException(spawned.name);
        _crewRef.Add(spawned);
        return spawned;
    };

    /// <summary>
    /// The read-only view of <paramref name="raw"/> the body sees: a Proxy whose <c>with</c>
    /// is <see cref="stateWith"/> and whose every other set is refused. <c>undefined</c> and
    /// <c>null</c> have nothing to wrap and come back as they are.
    /// </summary>
    private JsValue BuildStateProxy(JsValue raw)
    {
        if (raw.IsUndefined() || raw.IsNull()) return raw;

        _stateProxyFactory ??= _engineRef.Evaluate(StateProxyFactorySource);
        _rejectStateMutation ??= prop => throw JsHostError.Wrap(_engineRef, new StateMutationOutsideWithException(prop));
        return _engineRef.Invoke(_stateProxyFactory, [raw, stateWith, _rejectStateMutation]);
    }

    /// <summary>Releases the agent-scoped lock semaphores, the state mutex and the llm interrupt source.</summary>
    public void Dispose()
    {
        foreach (var sem in _locks.Values)
            sem.Dispose();
        _locks.Clear();
        _stateMutex.Dispose();
        llm.DisposeInterruptSource();
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
