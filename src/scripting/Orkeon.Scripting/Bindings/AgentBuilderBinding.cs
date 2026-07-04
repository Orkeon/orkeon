using Jint;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>agentBuilder()</c> function on a Jint engine.
/// </summary>
public static class AgentBuilderBinding
{
    /// <summary>Name of the global function exposed to scripts.</summary>
    public const string GlobalName = "agentBuilder";

    /// <summary>
    /// Adds <c>agentBuilder()</c> to <paramref name="engine"/>'s global scope.
    /// Each call returns a fresh <see cref="JsAgentBuilder"/>.
    /// </summary>
    public static void Register(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.SetValue(GlobalName, new Func<JsAgentBuilder>(() => new JsAgentBuilder()));
    }
}
