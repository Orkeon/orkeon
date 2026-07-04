using Jint;
using Orkeon.Scripting.Testing;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>test</c> namespace for <c>.ork.test.ts</c> files.
/// </summary>
public static class TestNamespaceBinding
{
    /// <summary>Name of the global namespace exposed to scripts.</summary>
    public const string GlobalName = "test";

    /// <summary>Adds <c>test</c> to <paramref name="engine"/>'s global scope.</summary>
    public static JsTestNamespace Register(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var ns = new JsTestNamespace();
        engine.SetValue(GlobalName, ns);
        return ns;
    }
}
