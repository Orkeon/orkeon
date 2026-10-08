using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// A default a page quotes beside the key it belongs to: <c>`Llm:MaxRetries` (10 by default)</c>,
/// <c>`ResolveSymlinks` (default `true`)</c>, a row <c>| `Orkeon:Rag:Rerank:TopN` | 5 | … |</c> of a
/// table with a "Default" column.
/// </summary>
/// <param name="Line">The line of the page, from 1.</param>
/// <param name="Written">The key as the page writes it: its path, or its last name alone.</param>
/// <param name="Entry">The key of the catalogue it names.</param>
/// <param name="Quoted">The default the page gives it, as written.</param>
/// <param name="Offset">Where the quote starts in the page, which orders two quotes of one line.</param>
internal sealed record DocumentedDefault(int Line, string Written, SettingsCatalogEntry Entry, string Quoted, int Offset)
{
    /// <summary>Whether the page says what the code does, units and separators set aside.</summary>
    public bool Agrees => DocumentedDefaults.Same(Entry, Quoted);
}

/// <summary>
/// Finds, in the prose and the tables a page writes by hand, the defaults it quotes beside a key of
/// the settings catalogue, so that a test holds each to the code. A key is known by its full path,
/// or by its last name when one key only of the catalogue ends with it — <c>ResolveSymlinks</c> is
/// <c>PathSecurity:ResolveSymlinks</c>, while <c>Enabled</c> could be any of thirty and is left alone.
/// </summary>
internal static partial class DocumentedDefaults
{
    private const string Literal =
        @"(?:`[^`\n]+`|""[^""\n]+""|-?\d[\d,_]*(?:\.\d+)?(?:\s?(?:MiB|MB|Mo|KB|Ko|GB|Go|ms|min|s)\b)?|\btrue\b|\bfalse\b)";

    private const string Key = @"`(?<key>[A-Za-z_][\w<>]*(?::[\w<>]+)*)`";

    private static readonly string[] s_defaultColumns = ["default", "défaut", "defaults", "valeur par défaut"];

    /// <summary>The defaults <paramref name="markdown"/> quotes beside a key of <paramref name="catalog"/>.</summary>
    public static IReadOnlyList<DocumentedDefault> In(string markdown, SettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(catalog);

        var names = catalog.Settings
            .GroupBy(entry => entry.Path[(entry.Path.LastIndexOf(':') + 1)..], StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var paths = catalog.Settings.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        SettingsCatalogEntry? Named(string written) =>
            paths.TryGetValue(written, out var entry) ? entry
            : !written.Contains(':', StringComparison.Ordinal) && names.TryGetValue(written, out var single) ? single
            : null;

        var text = DocumentationPages.HandWritten(markdown.Replace("\r\n", "\n", StringComparison.Ordinal));
        var found = new List<DocumentedDefault>();
        foreach (var pattern in new[] { KeyThenDefault(), KeyThenValueByDefault() })
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (Named(match.Groups["key"].Value) is { } entry)
                    found.Add(new DocumentedDefault(LineOf(text, match.Index), match.Groups["key"].Value, entry, match.Groups["value"].Value, match.Index));
            }
        }

        found.AddRange(InTables(text, paths));
        return [.. found.OrderBy(quote => quote.Offset)];
    }

    /// <summary>Whether <paramref name="quoted"/> is the default of <paramref name="entry"/>.</summary>
    public static bool Same(SettingsCatalogEntry entry, string quoted)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(quoted);
        if (CodeDefault(entry) is not { } code)
            return false;

        var said = quoted.Trim().Trim('`', '"').Trim();
        if (string.Equals(said, code, StringComparison.OrdinalIgnoreCase))
            return true;

        if (Quantity().Match(said) is not { Success: true } quantity
            || !decimal.TryParse(
                quantity.Groups["number"].Value.Replace(",", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return false;
        }

        var unit = quantity.Groups["unit"].Value;
        if (string.Equals(entry.Type, "duration", StringComparison.Ordinal))
        {
            return TimeSpan.TryParse(code, CultureInfo.InvariantCulture, out var duration) && unit switch
            {
                "ms" => duration == TimeSpan.FromMilliseconds((double)number),
                "s" => duration == TimeSpan.FromSeconds((double)number),
                "min" => duration == TimeSpan.FromMinutes((double)number),
                _ => false,
            };
        }

        var factor = unit switch
        {
            "KB" or "Ko" => 1024m,
            "MB" or "Mo" or "MiB" => 1024m * 1024m,
            "GB" or "Go" => 1024m * 1024m * 1024m,
            _ => 1m,
        };
        return decimal.TryParse(code, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value == number * factor;
    }

    /// <summary>The default of <paramref name="entry"/> as a page would write it — a text without its quotes —, or null when it has none a page can quote.</summary>
    public static string? CodeDefault(SettingsCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Default is not { } literal)
            return null;

        using var document = JsonDocument.Parse(literal);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.String => document.RootElement.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => document.RootElement.GetRawText(),
            _ => null,
        };
    }

    /// <summary>
    /// The rows of the tables that have a "Default" column and whose first cell is one key, by its
    /// full path: the cell of that column, when it opens on a value.
    /// </summary>
    private static IEnumerable<DocumentedDefault> InTables(string text, Dictionary<string, SettingsCatalogEntry> paths)
    {
        var lines = text.Split('\n');
        var starts = new int[lines.Length];
        for (var line = 1; line < lines.Length; line++)
            starts[line] = starts[line - 1] + lines[line - 1].Length + 1;

        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (!lines[index].TrimStart().StartsWith('|') || !TableRule().IsMatch(lines[index + 1]))
                continue;

            var header = Cells(lines[index]);
            var column = header.FindIndex(cell => s_defaultColumns.Contains(cell.Trim('*', ' '), StringComparer.OrdinalIgnoreCase));
            var row = index + 2;
            for (; row < lines.Length && lines[row].TrimStart().StartsWith('|'); row++)
            {
                var cells = Cells(lines[row]);
                if (column < 0 || column >= cells.Count)
                    continue;

                var keys = CodeSpan().Matches(cells[0]);
                if (keys.Count != 1 || !paths.TryGetValue(keys[0].Groups["code"].Value, out var entry))
                    continue;

                var value = LiteralAtStart().Match(cells[column]);
                // A cell in words — "none", "built-in", "—" — quotes nothing, unless the code has a default it hides.
                if (value.Success)
                    yield return new DocumentedDefault(row + 1, entry.Path, entry, value.Value, starts[row]);
                else if (CodeDefault(entry) is { Length: > 0 })
                    yield return new DocumentedDefault(row + 1, entry.Path, entry, cells[column], starts[row]);
            }

            index = row - 1;
        }
    }

    private static List<string> Cells(string line) =>
        [.. CellBar().Split(line.Trim().Trim('|')).Select(cell => cell.Trim())];

    private static int LineOf(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    /// <summary><c>`Key` (default `x`)</c>, <c>`Key`, default x</c>, <c>`Key` defaults to x</c>, and the same in French.</summary>
    [GeneratedRegex(Key + @"\s*(?:\(|,|—|:)?\s*(?:defaults? to|default(?: is|:)?|défaut(?: :)?|par défaut(?: :)?)\s*(?<value>" + Literal + ")", RegexOptions.IgnoreCase)]
    private static partial Regex KeyThenDefault();

    /// <summary><c>`Key` (10 by default)</c>, and the same in French.</summary>
    [GeneratedRegex(Key + @"\s*\(\s*(?<value>" + Literal + @")\s+(?:by default|par défaut)", RegexOptions.IgnoreCase)]
    private static partial Regex KeyThenValueByDefault();

    [GeneratedRegex("^" + Literal, RegexOptions.IgnoreCase)]
    private static partial Regex LiteralAtStart();

    [GeneratedRegex(@"^(?<number>-?\d[\d,_]*(?:\.\d+)?)\s?(?<unit>MiB|MB|Mo|KB|Ko|GB|Go|ms|min|s)?$")]
    private static partial Regex Quantity();

    [GeneratedRegex(@"^\s*\|[\s:|-]+\|\s*$")]
    private static partial Regex TableRule();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex CellBar();

    [GeneratedRegex("`(?<code>[^`]+)`")]
    private static partial Regex CodeSpan();
}
