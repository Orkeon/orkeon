using System.Text.RegularExpressions;

namespace Orkeon.Analysis.Core.Structural;

/// <summary>The one metavariable token grammar of patterns and fix templates: <c>$NAME</c>, <c>$$$NAME</c> (variadic), <c>$$</c> (a literal dollar).</summary>
internal static partial class MetavariableGrammar
{
    /// <summary>Groups: 1 variadic name, 2 single name; neither set means the <c>$$</c> escape.</summary>
    [GeneratedRegex(@"\$\$\$([A-Za-z_][A-Za-z0-9_]*)|\$\$|\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    public static partial Regex Token();

    /// <summary>The bare name a token binds, or null for the <c>$$</c> escape.</summary>
    public static string? NameOf(Match token)
    {
        if (token.Groups[1].Success) return token.Groups[1].Value;
        return token.Groups[2].Success ? token.Groups[2].Value : null;
    }

    /// <summary>The text the <c>$$</c> escape stands for.</summary>
    public const string LiteralDollar = "$";

    public static IEnumerable<string> NamesIn(string text) =>
        Token().Matches(text).Select(NameOf).OfType<string>();
}
