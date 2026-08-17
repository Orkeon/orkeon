using Jint;
using Jint.Native;
using Orkeon.Cli.Commands.Scripting.Loading;

namespace Orkeon.Cli.Commands.Scripting.Bindings;

/// <summary>
/// Installs the <c>defineAsyncCommand</c> global (design §4.2 / §8 item 6). Forwards each
/// declaration to <see cref="CommandDescriptorCollector.AddAsync"/>, the async counterpart
/// of <see cref="DefineCommandBinding"/>.
/// </summary>
public static class DefineAsyncCommandBinding
{
    /// <summary>Name of the global function exposed to scripts.</summary>
    public const string GlobalName = "defineAsyncCommand";

    /// <summary>
    /// Installs <c>defineAsyncCommand</c> on <paramref name="engine"/>, forwarding to
    /// <paramref name="collector"/>. Must be called BEFORE <c>engine.Evaluate(js)</c>.
    /// </summary>
    public static void Register(Engine engine, CommandDescriptorCollector collector)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(collector);

        engine.SetValue(GlobalName, new Action<JsValue>(collector.AddAsync));
    }
}
