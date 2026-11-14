using Jint;
using Jint.Runtime;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Tests.Internal;

/// <summary>
/// Reads a host refusal the way a script and a CLR caller see it once the refusal goes through
/// <see cref="JsHostError.Guard{T}"/> (SCR-25, #364).
/// </summary>
internal static class ScriptRefusal
{
    /// <summary>
    /// Runs <paramref name="call"/> inside a script's <c>try … catch</c> and returns what the
    /// <c>catch</c> saw: <c>instanceof Error|clrType|message</c>, or <c>not caught</c> when the call
    /// did not throw. The first field is <c>false</c> as well for an <c>InvalidScriptException</c>
    /// that is not an <c>InvalidScriptError</c>: every script refusal is that class (#366).
    /// </summary>
    public static string Caught(Engine engine, string call) => engine.Evaluate($$"""
        (() => {
            try { {{call}}; } catch (e) {
                const classed = e.clrType !== "InvalidScriptException" || e instanceof InvalidScriptError;
                return [e instanceof Error && classed, e.clrType, e.message].join("|");
            }
            return "not caught";
        })();
        """).AsString();

    /// <summary>
    /// Asserts that <paramref name="action"/> throws the JavaScript Error a guarded host member
    /// raises, carrying a <typeparamref name="TException"/> whose message contains
    /// <paramref name="messageContains"/>, and returns that CLR exception.
    /// </summary>
    public static TException Bridged<TException>(Action action, string messageContains)
        where TException : Exception
    {
        var thrown = Assert.Throws<JavaScriptException>(action);
        var clr = Assert.IsType<TException>(JsHostError.Unwrap(thrown.Error));
        Assert.Contains(messageContains, clr.Message, StringComparison.Ordinal);
        return clr;
    }
}
