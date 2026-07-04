using Jint;
using Jint.Native;
using Orkeon.Cli.Scripting.Loading;

namespace Orkeon.Cli.Scripting.Bindings;

/// <summary>
/// Registers <c>globalThis.defineCommand(desc)</c> on a Jint engine. The function
/// forwards each call to a <see cref="CommandDescriptorCollector"/> owned by the loader
/// for the script being evaluated.
/// </summary>
public static class DefineCommandBinding
{
    /// <summary>Name of the global function exposed to scripts.</summary>
    public const string GlobalName = "defineCommand";

    /// <summary>
    /// Installs <c>defineCommand</c> on <paramref name="engine"/>, forwarding to
    /// <paramref name="collector"/>. Must be called BEFORE <c>engine.Evaluate(js)</c>.
    /// </summary>
    public static void Register(Engine engine, CommandDescriptorCollector collector)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(collector);

        engine.SetValue(GlobalName, new Action<JsValue>(collector.Add));
    }
}
