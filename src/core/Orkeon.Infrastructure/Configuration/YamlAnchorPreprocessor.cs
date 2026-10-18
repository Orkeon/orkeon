using System.Text;
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
    [GeneratedRegex(@"^anchors[ \t]*:[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AnchorsHeaderRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<esc>\\?)\*(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AliasLineRegex();

    public static string Preprocess(string yaml) => Expand(yaml).Text;

    /// <summary>
    /// Expands the aliases of <paramref name="yaml"/> and records, for each line of the result, the
    /// line of the file it came from: the <c>anchors:</c> section is gone and an alias spans several
    /// lines, so a line number read on the result is not the file's.
    /// </summary>
    public static ExpandedYaml Expand(string yaml)
    {
        if (string.IsNullOrEmpty(yaml))
            return new ExpandedYaml(yaml, null);

        var header = AnchorsHeaderRegex().Match(yaml);
        if (!header.Success)
            return new ExpandedYaml(yaml, null);

        var (prefix, anchorsBlock, remainder) = SplitAnchorsSection(yaml, header.Index);
        var anchors = ParseAnchors(anchorsBlock);
        ValidateNoRecursion(anchors);

        var lines = new List<int>(Enumerable.Range(1, prefix.AsSpan().Count('\n')));
        var text = new StringBuilder(prefix);
        var fileLine = yaml.AsSpan(0, yaml.Length - remainder.Length).Count('\n') + 1;
        var first = true;
        foreach (var line in remainder.Split('\n'))
        {
            var expanded = AliasLineRegex().Replace(line, m => ExpandMatch(m, anchors, fileLine));
            if (!first)
                text.Append('\n');
            first = false;
            text.Append(expanded);
            lines.AddRange(Enumerable.Repeat(fileLine, expanded.AsSpan().Count('\n') + 1));
            fileLine++;
        }

        return new ExpandedYaml(text.ToString(), [.. lines]);
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

    private static string ExpandMatch(Match m, Dictionary<string, string> anchors, int fileLine)
    {
        var indent = m.Groups["indent"].Value;
        var name = m.Groups["name"].Value;
        var escaped = m.Groups["esc"].Value.Length > 0;

        if (escaped)
            return indent + "*" + name;

        if (!anchors.TryGetValue(name, out var value))
            throw new InvalidOperationException(
                $"Undefined YAML anchor alias '*{name}' at line {fileLine}.");

        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return indent + string.Join("\n" + indent, lines);
    }
}

/// <summary>A YAML text once its anchors are expanded, and where each of its lines was in the file.</summary>
/// <param name="Text">The expanded text.</param>
/// <param name="Lines">The file line of each line of <paramref name="Text"/>; null when the text is the file as read.</param>
internal sealed record ExpandedYaml(string Text, int[]? Lines)
{
    /// <summary>The file line of a line of <see cref="Text"/> (1-based).</summary>
    /// <param name="line">The line read on <see cref="Text"/>.</param>
    public int FileLine(long line)
        => Lines is { Length: > 0 } map ? map[(int)Math.Clamp(line, 1, map.Length) - 1] : (int)line;
}
