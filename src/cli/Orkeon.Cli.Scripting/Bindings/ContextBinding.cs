using Jint;
using Jint.Native;
using Orkeon.Cli.Scripting.Runtime;

namespace Orkeon.Cli.Scripting.Bindings;

/// <summary>
/// Helpers that prepare a Jint engine for a single handler invocation: re-binding the
/// console shim against the per-invocation logger and producing the <see cref="JsValue"/>
/// pair (args, ctx) that the handler will receive.
/// </summary>
public static class ContextBinding
{
    /// <summary>
    /// Re-binds <c>globalThis.console</c> to forward into <paramref name="ctx"/>.log,
    /// then returns the <c>ctx</c> as a <see cref="JsValue"/> usable as a handler arg.
    /// </summary>
    public static JsValue Push(Engine engine, CommandRuntimeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(ctx);

        ConsoleShimBinding.ApplyForInvocation(engine, ctx.log);
        return JsValue.FromObject(engine, ctx);
    }
}
