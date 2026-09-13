using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Orchestration;

/// <summary>
/// Script-scoped finite state machine produced by the <c>stateMachine(literal)</c>
/// global. Maps the literal declaration to a runtime instance with state lookup,
/// transition guards, and entry/exit hooks.
/// </summary>
/// <remarks>
/// <para><c>send</c> is a JS async function, not a CLR one (SCR-25 T2). A guard or a hook may
/// return a promise (<c>fsm.d.ts</c>), and awaiting it from CLR meant resuming on a thread-pool
/// thread while the agent body that called <c>send</c> was being drained synchronously on
/// another: the async side's wake-up was lost against that drain, and the next hook plus its
/// context object were built off the engine thread. With the loop in JS every hook runs as a
/// promise reaction on whichever thread drains the loop, and the CLR only supplies three
/// synchronous helpers — <c>lookup</c>, <c>current</c> and <c>commit</c> — that never call back
/// into the engine.</para>
/// <para>The helpers that can fail go through <see cref="JsHostError"/>: a raw CLR exception
/// thrown inside an async JS function skips <c>catch</c> and <c>finally</c> and leaves the
/// promise pending, whereas a bridged one rejects it like a script throw.</para>
/// </remarks>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of StateMachine in Typings/fsm.d.ts; that declaration is the contract scripts read.
public sealed class JsStateMachine
{
    /// <summary>
    /// The transition loop. The context objects are JS literals so nothing is converted after
    /// an await; a falsy guard result vetoes the transition and neither hook fires; an unknown
    /// event (<c>lookup</c> returns null) is ignored and <c>send</c> resolves to the current state.
    /// </summary>
    private const string SendFactorySource = """
        (lookup, current, commit) => async function send(eventName, payload) {
            const t = lookup(eventName);
            if (!t) return current();
            const ctx = { state: current(), payload };
            if (t.guard && !(await t.guard(ctx))) return current();
            if (t.onExit) await t.onExit(ctx);
            commit(t.target);
            const entered = { state: current(), payload };
            if (t.onEntry) await t.onEntry(entered);
            return current();
        }
        """;

    private readonly Engine _engine;
    private readonly Dictionary<string, JsFsmState> _states;
    private JsValue? _sendJs;

    public string name { get; }
    public string current { get; private set; }

    internal JsStateMachine(Engine engine, string name, string initial, Dictionary<string, JsFsmState> states)
    {
        _engine = engine;
        this.name = name;
        current = initial;
        _states = states;
    }

    public JsValue send => _sendJs ??= BuildSendFunction();

    private JsValue BuildSendFunction()
    {
        Func<string?, JsFsmTransitionView?> lookup = eventName => JsHostError.Guard(_engine, () => Lookup(eventName));
        Func<string> current = () => this.current;
        Action<string> commit = target => JsHostError.Guard(_engine, () => Commit(target));
        var factory = _engine.Evaluate(SendFactorySource);
        return _engine.Invoke(factory, [lookup, current, commit]);
    }

    /// <summary>
    /// Resolves <paramref name="eventName"/> against the current state: the transition it
    /// triggers with the hooks that frame it, or <see langword="null"/> for an event the state
    /// does not declare.
    /// </summary>
    private JsFsmTransitionView? Lookup(string? eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (!_states.TryGetValue(current, out var source))
            throw new InvalidOperationException($"Unknown FSM state '{current}'.");
        if (!source.Transitions.TryGetValue(eventName, out var transition))
            return null;
        var onEntry = _states.TryGetValue(transition.Target, out var target) ? target.OnEntry : null;
        return new JsFsmTransitionView(transition.Target, transition.Guard, source.OnExit, onEntry);
    }

    private void Commit(string target)
    {
        if (!_states.ContainsKey(target))
            throw new InvalidScriptException($"FSM transition target '{target}' is not declared.");
        current = target;
    }
}

/// <summary>
/// What the <c>send</c> trampoline reads off a resolved transition. The hooks are handed to
/// JS as they were declared; an absent one reads as <c>undefined</c>.
/// </summary>
internal sealed class JsFsmTransitionView
{
    public string target { get; }
    public JsValue guard { get; }
    public JsValue onExit { get; }
    public JsValue onEntry { get; }

    public JsFsmTransitionView(string target, JsValue? guard, JsValue? onExit, JsValue? onEntry)
    {
        this.target = target;
        this.guard = guard ?? JsValue.Undefined;
        this.onExit = onExit ?? JsValue.Undefined;
        this.onEntry = onEntry ?? JsValue.Undefined;
    }
}

internal sealed class JsFsmState
{
    public JsValue? OnEntry;
    public JsValue? OnExit;
    public Dictionary<string, JsFsmTransition> Transitions { get; } = new(StringComparer.Ordinal);
}

internal sealed class JsFsmTransition
{
    public string Target { get; }
    public JsValue? Guard { get; }
    public JsFsmTransition(string target, JsValue? guard) { Target = target; Guard = guard; }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
