using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Expands custom <c>anchors:</c> references that would otherwise be left literal
/// inside YAML <c>|</c> literal blocks (which are opaque to native YamlDotNet anchor
/// resolution). Runs before deserialization.
///
/// Given a top-level <c>anchors:</c> mapping of named multiline strings, the
/// preprocessor strips that section and replaces every <c>*name</c> token that sits
/// alone on its own line (indent only before it, optional whitespace after) with
/// the anchor's content, re-indented to match. The escape form <c>\*name</c> strips
/// the backslash and preserves <c>*name</c> as literal text.
/// </summary>
internal static partial class YamlAnchorPreprocessor
{
    [GeneratedRegex(@"^anchors[ \t]*:[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AnchorsHeaderRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<esc>\\?)\*(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AliasLineRegex();

    public static string Preprocess(string yaml)
    {
        if (string.IsNullOrEmpty(yaml))
            return yaml;

        var header = AnchorsHeaderRegex().Match(yaml);
        if (!header.Success)
            return yaml;

        var (prefix, anchorsBlock, remainder) = SplitAnchorsSection(yaml, header.Index);
        var anchors = ParseAnchors(anchorsBlock);
        ValidateNoRecursion(anchors);

        var expanded = AliasLineRegex().Replace(remainder, m => ExpandMatch(m, anchors));
        return prefix + expanded;
    }

    private static (string prefix, string anchorsBlock, string remainder) SplitAnchorsSection(
        string yaml, int headerIndex)
    {
        var prefix = yaml[..headerIndex];
        var afterHeader = headerIndex;
        var lineEnd = yaml.IndexOf('\n', afterHeader);
        var blockStart = lineEnd < 0 ? yaml.Length : lineEnd + 1;

        var i = blockStart;
        while (i < yaml.Length)
        {
            var nextNewline = yaml.IndexOf('\n', i);
            var endOfLine = nextNewline < 0 ? yaml.Length : nextNewline;
            var line = yaml[i..endOfLine];

            if (line.Length == 0 || line[0] == ' ' || line[0] == '\t' ||
                string.IsNullOrWhiteSpace(line))
            {
                i = nextNewline < 0 ? yaml.Length : nextNewline + 1;
                continue;
            }

            break;
        }

        var headerLine = yaml[headerIndex..blockStart];
        var anchorsBlock = headerLine + yaml[blockStart..i];
        var remainder = yaml[i..];
        return (prefix, anchorsBlock, remainder);
    }

    private static Dictionary<string, string> ParseAnchors(string anchorsBlock)
    {
        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(anchorsBlock)
                   ?? throw new InvalidOperationException("Failed to parse 'anchors:' section.");

        if (!root.TryGetValue("anchors", out var anchors) || anchors is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var normalized = new Dictionary<string, string>(anchors.Count, StringComparer.Ordinal);
        foreach (var (name, value) in anchors)
        {
            if (value is null) continue;
            normalized[name] = value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n', '\r');
        }
        return normalized;
    }

    private static void ValidateNoRecursion(IReadOnlyDictionary<string, string> anchors)
    {
        foreach (var (name, value) in anchors)
        {
            foreach (Match match in AliasLineRegex().Matches(value))
            {
                if (match.Groups["esc"].Value.Length == 0)
                    throw new InvalidOperationException(
                        $"Recursive YAML anchor references are not supported (in anchor '{name}').");
            }
        }
    }

    private static string ExpandMatch(Match m, Dictionary<string, string> anchors)
    {
        var indent = m.Groups["indent"].Value;
        var name = m.Groups["name"].Value;
        var escaped = m.Groups["esc"].Value.Length > 0;

        if (escaped)
            return indent + "*" + name;

        if (!anchors.TryGetValue(name, out var value))
            throw new InvalidOperationException(
                $"Undefined YAML anchor alias '*{name}' at offset {m.Index}.");

        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return indent + string.Join("\n" + indent, lines);
    }
}
