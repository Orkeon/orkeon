using Jint;
using Jint.Native;
using Orkeon.Scripting.ErrorPolicy;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>ErrorAction</c> namespace exposing factory methods that
/// build <see cref="JsErrorAction"/> instances. Script <c>onError</c> handlers return
/// these to the runtime.
/// </summary>
internal static class ErrorActionBinding
{
    /// <summary>Name of the global namespace exposed to scripts.</summary>
    public const string GlobalName = "ErrorAction";

    /// <summary>Adds <c>ErrorAction</c> to <paramref name="engine"/>'s global scope.</summary>
    public static void Register(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.SetValue(GlobalName, new JsErrorActionFactory(engine));
    }
}

/// <summary>
/// Surface exposed to scripts as <c>ErrorAction</c>: <c>fail()</c>, <c>retry({...})</c>,
/// <c>skip()</c>, <c>fallback(value)</c>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop factory for the ErrorAction union typed in Typings/agent.d.ts; the four members it exposes are named on the type summary above.
// Every member a script calls goes through Guard (SCR-25, #364): a delay out of range is an
// Error the script catches, not a CLR throw that crosses its try ... catch.
internal sealed class JsErrorActionFactory(Engine engine)
{
    public JsErrorAction fail() => JsHostError.Guard(engine, () => new JsErrorAction(JsErrorActionKind.Fail));
    public JsErrorAction skip() => JsHostError.Guard(engine, () => new JsErrorAction(JsErrorActionKind.Skip));
    public JsErrorAction fallback(JsValue value)
        => JsHostError.Guard(engine, () => new JsErrorAction(JsErrorActionKind.Fallback, fallback: value));

    public JsErrorAction retry(JsValue? options = null) => JsHostError.Guard(engine, () => Retry(options));

    private static JsErrorAction Retry(JsValue? options)
    {
        TimeSpan? delay = null;
        int? max = null;
        if (options is not null && options.IsObject())
        {
            var d = options.Get("delay");
            if (d.IsNumber()) delay = TimeSpan.FromMilliseconds(d.AsNumber());
            else if (d.IsString()) delay = ParseDuration(d.AsString());
            var m = options.Get("max"); if (m.IsNumber()) max = (int)m.AsNumber();
        }
        return new JsErrorAction(JsErrorActionKind.Retry, delay, max);
    }

    private static TimeSpan ParseDuration(string text)
    {
        text = text.Trim();
        var i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
        if (!double.TryParse(text[..i], System.Globalization.CultureInfo.InvariantCulture, out var v))
            return TimeSpan.Zero;
#pragma warning disable CA1308 // normalized unit key driving the switch below; lowercase is the required form, not a comparison normalization
        var unit = text[i..].Trim().ToLowerInvariant();
#pragma warning restore CA1308
        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(v),
            "s" => TimeSpan.FromSeconds(v),
            "m" => TimeSpan.FromMinutes(v),
            "h" => TimeSpan.FromHours(v),
            _ => TimeSpan.FromMilliseconds(v),
        };
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
