using Jint;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>toolBuilder()</c> function on a Jint engine.
/// </summary>
public static class ToolBuilderBinding
{
    /// <summary>Name of the global function exposed to scripts.</summary>
    public const string GlobalName = "toolBuilder";

    /// <summary>Adds <c>toolBuilder()</c> to <paramref name="engine"/>'s global scope.</summary>
    public static void Register(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.SetValue(GlobalName, new Func<JsToolBuilder>(() => new JsToolBuilder(engine)));
    }
}
