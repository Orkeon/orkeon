using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.Orchestration;

/// <summary>
/// Script-scoped finite state machine produced by the <c>stateMachine(literal)</c>
/// global. Maps the literal declaration to a runtime instance with state lookup,
/// transition guards, and entry/exit hooks.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of StateMachine in Typings/fsm.d.ts; that declaration is the contract scripts read.
public sealed class JsStateMachine
{
    private readonly Engine _engine;
    private readonly Dictionary<string, JsFsmState> _states;

    public string name { get; }
    public string current { get; private set; }

    internal JsStateMachine(Engine engine, string name, string initial, Dictionary<string, JsFsmState> states)
    {
        _engine = engine;
        this.name = name;
        current = initial;
        _states = states;
    }

    public Func<string, JsValue?, Task<string>> send => async (eventName, payload) =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (!_states.TryGetValue(current, out var src))
            throw new InvalidOperationException($"Unknown FSM state '{current}'.");
        if (!src.Transitions.TryGetValue(eventName, out var transition))
            return current; // ignore unknown events

        var ctxObj = JsValue.FromObject(_engine, new { state = current, payload = payload ?? JsValue.Undefined });
        if (transition.Guard is not null)
        {
            var guardResult = _engine.Invoke(transition.Guard, [ctxObj]);
            if (guardResult.IsPromise())
                guardResult = await guardResult.UnwrapIfPromiseAsync(CancellationToken.None).ConfigureAwait(false);
            if (!guardResult.AsBoolean()) return current;
        }

        await InvokeHookAsync(src.OnExit, ctxObj).ConfigureAwait(false);
        if (!_states.ContainsKey(transition.Target))
            throw new InvalidScriptException($"FSM transition target '{transition.Target}' is not declared.");
        current = transition.Target;
        var newCtx = JsValue.FromObject(_engine, new { state = current, payload = payload ?? JsValue.Undefined });
        await InvokeHookAsync(_states[current].OnEntry, newCtx).ConfigureAwait(false);
        return current;
    };

    private async Task InvokeHookAsync(JsValue? hook, JsValue ctx)
    {
        if (hook is null || hook.IsUndefined() || hook.IsNull()) return;
        var raw = _engine.Invoke(hook, [ctx]);
        if (raw.IsPromise())
            await raw.UnwrapIfPromiseAsync(CancellationToken.None).ConfigureAwait(false);
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
