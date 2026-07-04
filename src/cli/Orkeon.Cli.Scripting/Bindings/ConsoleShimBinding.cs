using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Scripting.Runtime;

namespace Orkeon.Cli.Scripting.Bindings;

/// <summary>
/// Replaces <c>globalThis.console</c> with an object that forwards
/// <c>log/info/warn/error/debug</c> to a <see cref="JsLogShim"/>. Lets devs keep their
/// reflex <c>console.log("hi")</c> calls while routing the output through the same logger
/// pipeline as <c>ctx.log</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two activation modes:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="ApplyForLoad"/> — installs a console pointing at a neutral
///     <see cref="ILogger"/> (or <see cref="NullLogger.Instance"/>) BEFORE
///     <c>engine.Evaluate(js)</c> at load time. Ensures `console.log` called during
///     module evaluation doesn't blow up and is not routed to a per-command ctx that
///     doesn't exist yet (spec §13 Phase 2 "au load, console.* doit pointer vers ILogger neutre").
///   </description></item>
///   <item><description>
///     <see cref="ApplyForInvocation"/> — re-binds console to a per-command logger shim
///     just BEFORE invoking a handler. Re-applied on every invocation so concurrent
///     commands (defensively serialised by ScriptCommand) never share a stale logger.
///   </description></item>
/// </list>
/// </remarks>
public static class ConsoleShimBinding
{
    /// <summary>JS global name overridden by this binding.</summary>
    public const string GlobalName = "console";

    /// <summary>Install a load-time console pointing at <paramref name="logger"/> (NullLogger if omitted).</summary>
    public static void ApplyForLoad(Engine engine, ILogger? logger = null)
        => Install(engine, new JsLogShim(logger ?? NullLogger.Instance));

    /// <summary>Re-bind console for the next handler invocation.</summary>
    public static void ApplyForInvocation(Engine engine, JsLogShim ctxLog)
        => Install(engine, ctxLog);

    private static void Install(Engine engine, JsLogShim shim)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(shim);

        // We don't wipe the previous console object — Jint setters replace it cleanly.
        var anonConsole = new ConsoleShim(shim);
        engine.SetValue(GlobalName, anonConsole);
    }
}

/// <summary>
/// CLR shim exposed to Jint as <c>globalThis.console</c>. Camel-case methods are
/// intentional — they're surfaced to JS via reflection.
/// </summary>
public sealed class ConsoleShim
{
    private readonly JsLogShim _shim;

    internal ConsoleShim(JsLogShim shim) { _shim = shim; }

#pragma warning disable IDE1006
    // CA1062: `message` is nullable by contract (JS callers may omit it); Render maps
    // null/undefined to string.Empty, so the rule is satisfied by coalescing rather than
    // a throw — preserving the original null-tolerant behaviour.
    public void log(JsValue? message, JsValue? data = null) => _shim.info(Render(message ?? JsValue.Undefined), data);
    public void info(JsValue? message, JsValue? data = null) => _shim.info(Render(message ?? JsValue.Undefined), data);
    public void debug(JsValue? message, JsValue? data = null) => _shim.debug(Render(message ?? JsValue.Undefined), data);
    public void warn(JsValue? message, JsValue? data = null) => _shim.warn(Render(message ?? JsValue.Undefined), data);
    public void error(JsValue? message, JsValue? data = null) => _shim.error(Render(message ?? JsValue.Undefined), data);
#pragma warning restore IDE1006

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Defensive rendering of an arbitrary JS value: any ToObject/ToString failure falls back to the raw JsValue string so logging a console message can never throw.")]
    private static string Render(JsValue? v)
    {
        if (v is null || v.IsUndefined() || v.IsNull()) return string.Empty;
        if (v.IsString()) return v.AsString();
        try
        {
            var clr = v.ToObject();
            return clr?.ToString() ?? string.Empty;
        }
        catch
        {
            return v.ToString() ?? string.Empty;
        }
    }
}
