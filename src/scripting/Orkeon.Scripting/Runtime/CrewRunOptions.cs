using Jint;
using Jint.Native;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Parses the <c>{ signal, timeout, inputs }</c> options object passed to
/// <c>crew.run(opts)</c>.
/// </summary>
internal static class CrewRunOptions
{
    public static (CancellationToken? Signal, TimeSpan? Timeout) From(JsValue? options)
    {
        if (options is null || options.IsUndefined() || options.IsNull())
            return (null, null);

        CancellationToken? signal = null;
        TimeSpan? timeout = null;

        if (options.IsObject())
        {
            var sig = options.Get("signal");
            if (!sig.IsUndefined() && !sig.IsNull() && sig.ToObject() is CancellationToken ct)
                signal = ct;

            var to = options.Get("timeout");
            if (!to.IsUndefined() && !to.IsNull())
            {
                if (to.IsNumber())
                    timeout = TimeSpan.FromMilliseconds(to.AsNumber());
                else if (to.IsString())
                    timeout = ParseDuration(to.AsString());
            }
        }

        return (signal, timeout);
    }

    private static TimeSpan ParseDuration(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TimeSpan.Zero;
        text = text.Trim();
        var (numericPart, unit) = SplitUnit(text);
        if (!double.TryParse(numericPart, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Invalid duration: '{text}'.");
        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(value),
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            "" => TimeSpan.FromMilliseconds(value),
            _ => throw new FormatException($"Unknown duration unit '{unit}' in '{text}'."),
        };
    }

    private static (string Numeric, string Unit) SplitUnit(string text)
    {
        int i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == ',' || text[i] == '-'))
            i++;
#pragma warning disable CA1308 // produces the normalized unit token consumers switch on; lowercase is the required form, not a comparison normalization
        return (text[..i], text[i..].Trim().ToLowerInvariant());
#pragma warning restore CA1308
    }
}
