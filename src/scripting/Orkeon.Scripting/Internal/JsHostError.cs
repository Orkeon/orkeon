using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Orkeon.Scripting.Internal;

/// <summary>
/// The one way a CLR helper called from a JS trampoline reports a failure (SCR-25).
/// </summary>
/// <remarks>
/// <para>Under the engine's default interop policy a CLR exception thrown by a delegate does not
/// become a JavaScript exception: it unwinds straight through the script — no <c>catch</c>, no
/// <c>finally</c> — leaves the enclosing async function's promise pending forever, and erupts
/// out of whichever CLR frame happened to be draining the event loop (measured on Jint 4.16.1,
/// 2026-09-13). A trampoline whose <c>finally</c> releases a semaphore or ends a run cannot
/// live with that. Jint's <c>Interop.ExceptionHandler</c> is not the answer either: it turns
/// every exception into a bare <c>Error</c> and drops the CLR object, including one bridged
/// deliberately.</para>
/// <para>So the bridge is explicit. <see cref="Wrap"/> builds a JS <c>Error</c> that carries the
/// CLR exception on its <c>clr</c> property and returns it as a <see cref="JavaScriptException"/>
/// — thrown from a delegate, that IS a JavaScript throw: the script's <c>catch</c> and
/// <c>finally</c> run, the promise rejects with the Error, and <see cref="Unwrap"/> gives the
/// CLR side its typed exception back (<c>JsCrew.TryUnwrapTypedHostException</c>, the error
/// policy's code mapping, the cancellation rule at the root pumps).</para>
/// <para>A faulted <see cref="Task"/> awaited by the script needs none of this: Jint's task
/// bridge rejects the promise with the <see cref="AggregateException"/> as a wrapped CLR object,
/// which <see cref="Unwrap"/> also recognises.</para>
/// </remarks>
internal static class JsHostError
{
    private const string ClrProperty = "clr";

    /// <summary>The Error factory; evaluated per engine by <see cref="JsTrampolineFactories.HostError"/>.</summary>
    internal const string FactorySource =
        "(message, clr, clrType) => { const err = new Error(message); err.clr = clr; err.clrType = clrType; return err; }";

    /// <summary>
    /// A JavaScript throw of <paramref name="exception"/>: an <c>Error</c> with the CLR message,
    /// the CLR exception on <c>clr</c> and its type name on <c>clrType</c>. A
    /// <see cref="JavaScriptException"/> is returned as it is — it already is a JS throw.
    /// </summary>
    public static JavaScriptException Wrap(Engine engine, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is JavaScriptException already)
            return already;

        var factory = JsTrampolineFactories.HostError.For(engine);
        var error = engine.Invoke(factory, exception.Message, exception, exception.GetType().Name);
        return new JavaScriptException(error);
    }

    /// <summary>
    /// The CLR exception <paramref name="value"/> carries, or <see langword="null"/> for a value
    /// that is not a bridged Error nor a wrapped CLR exception (a script's own <c>throw</c>).
    /// </summary>
    public static Exception? Unwrap(JsValue? value)
    {
        if (value is null || !value.IsObject())
            return null;
        if (value.ToObject() is Exception direct)
            return direct;
        var clr = value.AsObject().Get(ClrProperty);
        return clr.IsObject() ? clr.ToObject() as Exception : null;
    }

    /// <summary>
    /// Runs <paramref name="body"/> and bridges anything it throws, so a CLR helper handed to a
    /// trampoline never unwinds through the script.
    /// </summary>
    public static T Guard<T>(Engine engine, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is not JavaScriptException)
        {
            throw Wrap(engine, ex);
        }
    }

    /// <inheritdoc cref="Guard{T}"/>
    public static void Guard(Engine engine, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            body();
        }
        catch (Exception ex) when (ex is not JavaScriptException)
        {
            throw Wrap(engine, ex);
        }
    }
}
