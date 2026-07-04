using Jint;
using Jint.Native;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Orchestration;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>stateMachine(literal)</c> factory on a Jint engine.
/// </summary>
public static class StateMachineBinding
{
    /// <summary>Name of the global function exposed to scripts.</summary>
    public const string GlobalName = "stateMachine";

    /// <summary>Adds <c>stateMachine</c> to <paramref name="engine"/>'s global scope.</summary>
    public static void Register(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.SetValue(GlobalName, new Func<JsValue, JsStateMachine>(literal => Build(engine, literal)));
    }

    private static JsStateMachine Build(Engine engine, JsValue literal)
    {
        if (literal is null || !literal.IsObject())
            throw new InvalidScriptException("stateMachine() expects a literal { name, initial, states }.");

        var nameVal = literal.Get("name");
        var initialVal = literal.Get("initial");
        var statesVal = literal.Get("states");

        if (!nameVal.IsString())
            throw new InvalidScriptException("stateMachine literal requires a string 'name'.");
        if (!initialVal.IsString())
            throw new InvalidScriptException("stateMachine literal requires a string 'initial'.");
        if (!statesVal.IsObject())
            throw new InvalidScriptException("stateMachine literal requires a 'states' object.");

        var states = BuildStates(statesVal);

        var initial = initialVal.AsString();
        if (!states.ContainsKey(initial))
            throw new InvalidScriptException($"FSM 'initial' state '{initial}' is not declared in 'states'.");

        ValidateTransitionTargets(states);

        return new JsStateMachine(engine, nameVal.AsString(), initial, states);
    }

    private static Dictionary<string, JsFsmState> BuildStates(JsValue statesVal)
    {
        var states = new Dictionary<string, JsFsmState>(StringComparer.Ordinal);
        foreach (var prop in statesVal.AsObject().GetOwnProperties())
        {
            var stateName = prop.Key.ToString()!;
            var stateLiteral = prop.Value.Value;
            var fsmState = new JsFsmState
            {
                OnEntry = OptionalCallback(stateLiteral, "onEntry"),
                OnExit = OptionalCallback(stateLiteral, "onExit"),
            };
            PopulateTransitions(stateName, stateLiteral, fsmState);
            states[stateName] = fsmState;
        }
        return states;
    }

    private static void PopulateTransitions(string stateName, JsValue stateLiteral, JsFsmState fsmState)
    {
        var transitions = stateLiteral.Get("transitions");
        if (!transitions.IsObject())
            return;

        foreach (var trProp in transitions.AsObject().GetOwnProperties())
        {
            var evtName = trProp.Key.ToString()!;
            var tr = trProp.Value.Value;
            var target = tr.Get("target");
            if (!target.IsString())
                throw new InvalidScriptException($"FSM transition '{stateName}.{evtName}' missing string target.");
            fsmState.Transitions[evtName] = new JsFsmTransition(target.AsString(), OptionalCallback(tr, "guard"));
        }
    }

    private static void ValidateTransitionTargets(Dictionary<string, JsFsmState> states)
    {
        foreach (var (sn, st) in states)
        {
            foreach (var (evt, tr) in st.Transitions)
            {
                if (!states.ContainsKey(tr.Target))
                    throw new InvalidScriptException(
                        $"FSM transition '{sn}.{evt}' targets undeclared state '{tr.Target}'.");
            }
        }
    }

    private static JsValue? OptionalCallback(JsValue parent, string key)
    {
        var v = parent.Get(key);
        return v.IsUndefined() || v.IsNull() ? null : v;
    }
}
