using Jint.Native;

namespace Orkeon.Scripting.ErrorPolicy;

/// <summary>
/// Decision returned from <c>onError</c> handlers. Scripts construct one via the
/// <c>ErrorAction</c> namespace exposed globally (see <see cref="Bindings.JsErrorActionFactory"/>).
/// </summary>
public enum JsErrorActionKind
{
    /// <summary>Re-raise the exception.</summary>
    Fail,
    /// <summary>Re-invoke the failing body, optionally after a delay.</summary>
    Retry,
    /// <summary>Skip the failing body and continue with a null output.</summary>
    Skip,
    /// <summary>Continue with the supplied substitute value.</summary>
    Fallback,
}

/// <summary>
/// Result of an <c>onError</c> handler. Use the <c>ErrorAction</c> namespace from JS
/// to build instances (see <c>JsErrorActionFactory</c>); instances flow back into the
/// runtime which honours them.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsErrorAction
{
    public JsErrorActionKind kind { get; }
    public TimeSpan? delay { get; }
    public int? max { get; }
    public JsValue? fallbackValue { get; }

    internal JsErrorAction(JsErrorActionKind kind, TimeSpan? delay = null, int? max = null, JsValue? fallback = null)
    {
        this.kind = kind;
        this.delay = delay;
        this.max = max;
        fallbackValue = fallback;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
