using Jint;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>taskBuilder()</c> function on a Jint engine.
/// </summary>
public static class TaskBuilderBinding
{
    /// <summary>Name of the global function exposed to scripts.</summary>
    public const string GlobalName = "taskBuilder";

    /// <summary>Adds <c>taskBuilder()</c> to <paramref name="engine"/>'s global scope.</summary>
    public static void Register(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.SetValue(GlobalName, new Func<JsTaskBuilder>(() => new JsTaskBuilder()));
    }
}
