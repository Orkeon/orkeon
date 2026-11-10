using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// The keys the firefox tool presses: a character, a named key (<c>Enter</c>, <c>ArrowDown</c>) or a
/// chord (<c>Control+a</c>), in the code points WebDriver gives the keys that are not characters.
/// </summary>
internal static class FirefoxKeys
{
    public const string Enter = "";
    public const string Tab = "";
    public const string Backspace = "";

    private static readonly FrozenDictionary<string, string> s_modifiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Shift"] = "",
        ["Control"] = "",
        ["Ctrl"] = "",
        ["Alt"] = "",
        ["Meta"] = "",
        ["Cmd"] = "",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> s_named = Named().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names <see cref="TryParse"/> reads, for an error message.</summary>
    public static string Names => "Enter, Tab, Escape, Backspace, Delete, Space, ArrowUp, ArrowDown, ArrowLeft, ArrowRight, Home, End, PageUp, PageDown, Insert, F1-F12, one character, or a chord such as Control+a";

    /// <summary>
    /// Reads <paramref name="key"/>: the modifiers to hold, then the key to press, each as the value
    /// WebDriver expects.
    /// </summary>
    public static bool TryParse(string? key, [NotNullWhen(true)] out IReadOnlyList<string>? chord)
    {
        chord = null;
        if (string.IsNullOrEmpty(key))
            return false;

        var parts = SplitParts(key);

        var values = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!s_modifiers.TryGetValue(parts[i].Trim(), out var modifier))
                return false;
            values.Add(modifier);
        }

        var last = parts[^1];
        if (s_named.TryGetValue(last.Trim(), out var named))
            values.Add(named);
        else if (new StringInfo(last).LengthInTextElements == 1)
            values.Add(last);
        else
            return false;

        chord = values;
        return true;
    }

    /// <summary>The parts of a chord. A lone plus, and the last part of Control++, are the plus key.</summary>
    private static string[] SplitParts(string key)
    {
        if (key.Length > 1 && key.EndsWith("++", StringComparison.Ordinal))
            return [.. key[..^2].Split('+'), "+"];
        return key.Length == 1 ? [key] : key.Split('+');
    }

    /// <summary>
    /// The keys that type <paramref name="text"/>, one per character: a line feed is Enter, a tab is Tab,
    /// a carriage return is dropped.
    /// </summary>
    public static IEnumerable<string> Typing(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var rune in text.EnumerateRunes().Where(rune => rune.Value != '\r'))
        {
            yield return rune.Value switch
            {
                '\n' => Enter,
                '\t' => Tab,
                _ => rune.ToString(),
            };
        }
    }

    private static Dictionary<string, string> Named()
    {
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enter"] = Enter,
            ["Return"] = Enter,
            ["Tab"] = Tab,
            ["Backspace"] = Backspace,
            ["Escape"] = "",
            ["Esc"] = "",
            ["Space"] = " ",
            ["PageUp"] = "",
            ["PageDown"] = "",
            ["End"] = "",
            ["Home"] = "",
            ["ArrowLeft"] = "",
            ["ArrowUp"] = "",
            ["ArrowRight"] = "",
            ["ArrowDown"] = "",
            ["Insert"] = "",
            ["Delete"] = "",
        };
        for (var f = 1; f <= 12; f++)
            named[string.Create(CultureInfo.InvariantCulture, $"F{f}")] = new Rune(0xE030 + f).ToString();
        return named;
    }
}
