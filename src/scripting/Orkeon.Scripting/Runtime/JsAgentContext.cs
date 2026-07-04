using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Surface exposed to JS as <c>ctx</c> in agent <c>body</c> callbacks. Extends
/// <see cref="JsExecutionContext"/> with state, agent-scoped memory, agent-scoped
/// locks and dynamic spawn. Hooks (<c>onAgentStart</c>, <c>onAgentStop</c>) receive
/// the parent <see cref="JsExecutionContext"/> instead.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsAgentContext : JsExecutionContext, IDisposable
{
    private readonly Engine _engineRef;
    private readonly JsAgent _self;
    private readonly JsCrew _crewRef;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SemaphoreSlim _stateMutex = new(1, 1);
    private JsValue _stateRaw;

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
        state = BuildStateProxy();
    }

    public Func<JsValue, Task<JsValue>> stateWith => async transform =>
    {
        await _stateMutex.WaitAsync(signal).ConfigureAwait(false);
        try
        {
            var result = _engineRef.Invoke(transform, [_stateRaw]);
            var unwrapped = result.IsPromise()
                ? await result.UnwrapIfPromiseAsync(signal).ConfigureAwait(false)
                : result;
            _stateRaw = unwrapped;
            state = BuildStateProxy();
            return state;
        }
        finally
        {
            _stateMutex.Release();
        }
    };

    private JsValue? _lockJs;
    // Implemented in JS rather than C# to avoid nesting `UnwrapIfPromise` inside an
    // engine callback: Jint's `EventLoop.RunAvailableContinuations` is guarded by an
    // `_isProcessing` CAS, so a re-entrant unwrap from a C# delegate that is itself
    // running as part of an outer pump can't drain microtasks and dead-locks until the
    // 10s `PromiseTimeout` expires. The JS wrapper only awaits real Tasks (acquire) and
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

    private JsValue BuildStateProxy()
    {
        if (_stateRaw.IsUndefined() || _stateRaw.IsNull()) return _stateRaw;

        // Build a JS Proxy that delegates `with` to our C# stateWith closure and rejects
        // any other set. The trap calls a host function that throws the typed CLR
        // exception so callers can `Assert.ThrowsAsync<StateMutationOutsideWithException>`
        // instead of string-matching a JS Error.
        var withCallback = JsValue.FromObject(_engineRef, stateWith);
        Action<string> reject = prop => throw new StateMutationOutsideWithException(prop);
        _engineRef.SetValue("__currentState", _stateRaw);
        _engineRef.SetValue("__withCallback", withCallback);
        _engineRef.SetValue("__rejectStateMutation", reject);
        var script = """
            (function() {
                return new Proxy(__currentState, {
                    get(target, prop) {
                        if (prop === 'with') return __withCallback;
                        return target[prop];
                    },
                    set(target, prop, value) {
                        __rejectStateMutation(String(prop));
                        return true;
                    }
                });
            })()
            """;
        return _engineRef.Evaluate(script);
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
