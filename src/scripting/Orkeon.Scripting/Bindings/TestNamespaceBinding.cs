using Jint;
using Orkeon.Scripting.Testing;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>test</c> namespace for <c>.ork.test.ts</c> files.
/// </summary>
internal static class TestNamespaceBinding
{
    /// <summary>Name of the global namespace exposed to scripts.</summary>
    public const string GlobalName = "test";

    /// <summary>Adds <c>test</c> to <paramref name="engine"/>'s global scope.</summary>
    public static JsTestNamespace Register(Engine engine) => Register(engine, null);

    /// <summary>
    /// Adds <c>test</c> to <paramref name="engine"/>'s global scope, working on the mock of
    /// <paramref name="provider"/> — the provider the run's calls go through — when one is given.
    /// </summary>
    /// <param name="engine">The engine the global is added to.</param>
    /// <param name="provider">The run's test provider, or null for a namespace with a mock of its own.</param>
    public static JsTestNamespace Register(Engine engine, TestOverrideLlmProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var ns = new JsTestNamespace(provider?.Mock, engine);
        engine.SetValue(GlobalName, ns);
        return ns;
    }
}
