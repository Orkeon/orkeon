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
public sealed class JsErrorAction
{
    /// <summary>Which of the four decisions the handler returned.</summary>
    public JsErrorActionKind kind { get; }

    /// <summary>Delay before the retry, or <see langword="null"/> to retry immediately.</summary>
    public TimeSpan? delay { get; }

    /// <summary>Attempt ceiling: the runtime rethrows once <c>attempt</c> reaches it; <see langword="null"/> leaves retries uncapped.</summary>
    public int? max { get; }

    /// <summary>Substitute value for <see cref="JsErrorActionKind.Fallback"/>; <see langword="null"/> otherwise.</summary>
    public JsValue? fallbackValue { get; }

    internal JsErrorAction(JsErrorActionKind kind, TimeSpan? delay = null, int? max = null, JsValue? fallback = null)
    {
        this.kind = kind;
        this.delay = delay;
        this.max = max;
        fallbackValue = fallback;
    }
}
#pragma warning restore IDE1006
