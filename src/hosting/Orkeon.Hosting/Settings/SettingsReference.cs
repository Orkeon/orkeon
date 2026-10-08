using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Constants.Configuration;

namespace Orkeon.Hosting;

/// <summary>
/// The tables of the settings reference page (<c>docs/reference/configuration.md</c> and its French
/// mirror), produced from the settings catalogue: for each section the line that says who reads it
/// and one row per key — key, type, default, allowed values, meaning —, and the index of the
/// categories. The prose of the page is written by hand; what sits between two markers is not:
/// <c>&lt;!-- settings:RateLimiting --&gt;</c> … <c>&lt;!-- /settings --&gt;</c> for a section,
/// <c>&lt;!-- settings-index --&gt;</c> … <c>&lt;!-- /settings-index --&gt;</c> for the index.
/// <para>
/// It reads no file and writes none: <see cref="Apply"/> takes the page's text and returns it with
/// every block produced again, and a test holds the committed pages to that.
/// </para>
/// </summary>
internal sealed partial class SettingsReference
{
    private readonly SettingsCatalog _catalog;
    private readonly SettingsReferenceLanguage _language;

    public SettingsReference(SettingsCatalog catalog, SettingsReferenceLanguage language)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(language);
        _catalog = catalog;
        _language = language;
    }

    /// <summary>The marker that opens the block of the section at <paramref name="sectionPath"/>.</summary>
    public static string OpeningMarker(string sectionPath) => $"<!-- settings:{sectionPath} -->";

    /// <summary>The marker that closes a section's block.</summary>
    public const string ClosingMarker = "<!-- /settings -->";

    /// <summary>The marker that opens the index of the categories.</summary>
    public const string IndexOpeningMarker = "<!-- settings-index -->";

    /// <summary>The marker that closes the index of the categories.</summary>
    public const string IndexClosingMarker = "<!-- /settings-index -->";

    /// <summary>
    /// The anchor a Markdown renderer gives a heading, the way GitHub and the documentation site
    /// compute it: lower case, letters, digits, hyphens and underscores kept, a space a hyphen,
    /// everything else dropped — <c>`Orkeon:CostTracking`</c> is <c>orkeoncosttracking</c>.
    /// </summary>
    public static string Anchor(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);
        var anchor = new StringBuilder(heading.Length);
        foreach (var character in heading.Trim())
        {
            // Lower case is the form of an anchor, not a way to compare two texts.
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
                anchor.Append(char.ToLowerInvariant(character));
            else if (character == ' ')
                anchor.Append('-');
        }

        return anchor.ToString();
    }

    /// <summary>The heading of a section in the page: its path, as code — so its anchor is the same in every language.</summary>
    public static string SectionHeading(string sectionPath) => $"### `{sectionPath}`";

    /// <summary>The heading of a category in the page, in this reference's language.</summary>
    public string CategoryHeading(SettingsCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return $"## {_language.CategoryTitle(category)}";
    }

    /// <summary>
    /// What stands between the two markers of the section at <paramref name="sectionPath"/>: who
    /// reads it, then one row per key, in the catalogue's order. Ends with a line feed.
    /// </summary>
    /// <exception cref="ArgumentException">The catalogue holds no such section.</exception>
    public string Section(string sectionPath)
    {
        var section = _catalog.Section(sectionPath)
            ?? throw new ArgumentException($"The settings catalogue holds no section '{sectionPath}'.", nameof(sectionPath));
        var entries = _catalog.SettingsOf(section.Path).ToList();
        var withValues = entries.Exists(entry => entry.Values.Count > 0);

        var text = new StringBuilder();
        text.Append(_language.ReadBy(section)).Append("\n\n");

        text.Append("| ").Append(_language.Key).Append(" | ").Append(SettingsReferenceLanguage.Type).Append(" | ").Append(_language.Default);
        if (withValues)
            text.Append(" | ").Append(_language.Values);
        text.Append(" | ").Append(_language.Meaning).Append(" |\n");
        text.Append(withValues ? "|---|---|---|---|---|\n" : "|---|---|---|---|\n");

        foreach (var entry in entries)
        {
            text.Append("| ").Append(Code(_language.Placeholders(RelativeKey(section.Path, entry.Path))));
            text.Append(" | ").Append(_language.TypeName(entry.Type));
            if (entry.Secret)
                text.Append(", ").Append(SettingsReferenceLanguage.Secret);
            text.Append(" | ").Append(DefaultCell(entry));
            if (withValues)
                text.Append(" | ").Append(string.Join(", ", entry.Values.Select(Code)));
            text.Append(" | ").Append(Prose(_language.Describe(entry))).Append(" |\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// What stands between the two markers of the index: one row per category — its title, linked to
    /// its part of the page, its sections, each linked to its own, and its number of keys —, then the
    /// totals and the sections no shipped binary reads. Ends with a line feed.
    /// </summary>
    public string Index()
    {
        var text = new StringBuilder();
        text.Append("| ").Append(_language.Category).Append(" | ").Append(SettingsReferenceLanguage.Sections).Append(" | ").Append(_language.Keys).Append(" |\n");
        text.Append("|---|---|---|\n");
        foreach (var category in SettingsCategories.All)
        {
            var sections = _catalog.Sections.Where(section => string.Equals(section.Category, category.Id, StringComparison.Ordinal)).ToList();
            if (sections.Count == 0)
                continue;

            var keys = sections.Sum(section => _catalog.SettingsOf(section.Path).Count());
            text.Append("| [").Append(_language.CategoryTitle(category)).Append("](#").Append(Anchor(_language.CategoryTitle(category))).Append(") | ");
            text.AppendJoin(", ", sections.Select(SectionLink));
            text.Append(" | ").Append(keys).Append(" |\n");
        }

        var library = _catalog.Sections.Where(section => section.Hosts.Count == 0).ToList();
        text.Append('\n').Append(_language.Totals(_catalog.Sections.Count, _catalog.Settings.Count, library.Count));
        if (library.Count > 0)
            text.Append(' ').AppendJoin(", ", library.Select(SectionLink)).Append('.');
        text.Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// <paramref name="page"/> with every block produced again: the index, and each section's table.
    /// A block that names a section the catalogue does not hold is left as it is —
    /// <see cref="Problems"/> reports it.
    /// </summary>
    public string Apply(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var normalized = page.Replace("\r\n", "\n", StringComparison.Ordinal);
        var withIndex = IndexBlock().Replace(
            normalized,
            _ => $"{IndexOpeningMarker}\n{Index()}{IndexClosingMarker}");
        return SectionBlock().Replace(
            withIndex,
            block => _catalog.Section(block.Groups["path"].Value) is { } section
                ? $"{OpeningMarker(section.Path)}\n{Section(section.Path)}{ClosingMarker}"
                : block.Value);
    }

    /// <summary>
    /// The blocks of <paramref name="page"/> that are not what <see cref="Apply"/> would write, by
    /// what they hold: the path of a section, or "the index of the categories". Empty when the page
    /// is current — what a failure names, so that it sends to the section that changed.
    /// </summary>
    public IReadOnlyList<string> StaleBlocks(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var normalized = page.Replace("\r\n", "\n", StringComparison.Ordinal);
        var stale = new List<string>();
        if (IndexBlock().Match(normalized) is { Success: true } index
            && !string.Equals(index.Value, $"{IndexOpeningMarker}\n{Index()}{IndexClosingMarker}", StringComparison.Ordinal))
        {
            stale.Add("the index of the categories");
        }

        foreach (Match block in SectionBlock().Matches(normalized))
        {
            if (_catalog.Section(block.Groups["path"].Value) is { } section
                && !string.Equals(block.Groups["body"].Value, Section(section.Path), StringComparison.Ordinal))
            {
                stale.Add(section.Path);
            }
        }

        return stale;
    }

    /// <summary>
    /// What keeps <paramref name="page"/> from being the reference of this catalogue, one sentence
    /// each: a section without a block or with two, a block that names no section, a section or a
    /// category without its heading, a missing index. Empty when the page has a place for everything.
    /// </summary>
    public IReadOnlyList<string> Problems(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var normalized = page.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n').Select(line => line.TrimEnd()).ToHashSet(StringComparer.Ordinal);
        var blocks = SectionBlock().Matches(normalized).Select(block => block.Groups["path"].Value).ToList();
        var problems = new List<string>();

        if (IndexBlock().Count(normalized) != 1)
            problems.Add($"The page holds no index of the categories, or more than one: {IndexOpeningMarker} … {IndexClosingMarker}.");

        foreach (var category in SettingsCategories.All)
        {
            if (_catalog.Sections.Any(section => string.Equals(section.Category, category.Id, StringComparison.Ordinal))
                && !lines.Contains(CategoryHeading(category)))
            {
                problems.Add($"The category '{category.Id}' has no heading: {CategoryHeading(category)}");
            }
        }

        foreach (var section in _catalog.Sections)
        {
            var count = blocks.Count(path => string.Equals(path, section.Path, StringComparison.Ordinal));
            if (count == 0)
                problems.Add($"The section '{section.Path}' has no table: {OpeningMarker(section.Path)} … {ClosingMarker}, under {SectionHeading(section.Path)}.");
            else if (count > 1)
                problems.Add($"The section '{section.Path}' has {count} tables: one is enough.");

            if (!lines.Contains(SectionHeading(section.Path)))
                problems.Add($"The section '{section.Path}' has no heading: {SectionHeading(section.Path)}");
        }

        foreach (var path in blocks.Distinct(StringComparer.Ordinal))
        {
            if (_catalog.Section(path) is not { } known || !string.Equals(known.Path, path, StringComparison.Ordinal))
                problems.Add($"The block '{OpeningMarker(path)}' names a section the settings catalogue does not hold.");
        }

        return problems;
    }

    /// <summary>
    /// The keys the tables of <paramref name="page"/> list, by their full path as the catalogue
    /// writes it: the first cell of each row of each section's block, under the block's section.
    /// </summary>
    public IReadOnlyList<string> KeysListed(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var normalized = page.Replace("\r\n", "\n", StringComparison.Ordinal);
        var keys = new List<string>();
        foreach (Match block in SectionBlock().Matches(normalized))
        {
            var section = block.Groups["path"].Value;
            foreach (Match row in KeyCell().Matches(block.Groups["body"].Value))
                keys.Add(FullKey(section, _language.CataloguePlaceholders(row.Groups["key"].Value)));
        }

        return keys;
    }

    /// <summary>The key as a row writes it: what follows the section's path, or the path itself when the section is its own key.</summary>
    internal static string RelativeKey(string sectionPath, string keyPath) =>
        keyPath.Length > sectionPath.Length
        && keyPath.StartsWith(sectionPath, StringComparison.OrdinalIgnoreCase)
        && keyPath[sectionPath.Length] == ':'
            ? keyPath[(sectionPath.Length + 1)..]
            : keyPath;

    private static string FullKey(string sectionPath, string relativeKey) =>
        string.Equals(relativeKey, sectionPath, StringComparison.OrdinalIgnoreCase) ? sectionPath : $"{sectionPath}:{relativeKey}";

    private static string SectionLink(SettingsCatalogSection section) =>
        $"[`{section.Path}`](#{Anchor(section.Path)})";

    private string DefaultCell(SettingsCatalogEntry entry)
    {
        var note = entry.DefaultNote is null ? null : Prose(_language.DefaultNote(entry.DefaultNote));
        if (entry.Default is null)
            return note ?? "—";

        var literal = Code(Literal(entry.Default));
        return note is null ? literal : $"{literal} — {note}";
    }

    /// <summary>A default as one line: a list as its items between brackets, whatever the indentation it was stored with.</summary>
    private static string Literal(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? "[" + string.Join(", ", document.RootElement.EnumerateArray().Select(item => item.GetRawText())) + "]"
            : document.RootElement.GetRawText();
    }

    /// <summary>A value as code in a table cell: a bar would end the cell, so it is escaped.</summary>
    private static string Code(string value) =>
        "`" + value.Replace("|", "\\|", StringComparison.Ordinal) + "`";

    /// <summary>
    /// A sentence in a table cell: a bar is escaped, and an angle bracket outside code is written
    /// as an entity — a renderer would take <c>&lt;name&gt;</c> for a tag.
    /// </summary>
    private static string Prose(string sentence)
    {
        var text = new StringBuilder(sentence.Length + 8);
        var inCode = false;
        foreach (var character in sentence)
        {
            switch (character)
            {
                case '`':
                    inCode = !inCode;
                    text.Append(character);
                    break;
                case '|':
                    text.Append("\\|");
                    break;
                case '<' when !inCode:
                    text.Append("&lt;");
                    break;
                case '>' when !inCode:
                    text.Append("&gt;");
                    break;
                case '\n' or '\r':
                    text.Append(' ');
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }

        return text.ToString();
    }

    [GeneratedRegex(@"<!-- settings:(?<path>[^\s]+) -->\n(?<body>.*?)<!-- /settings -->", RegexOptions.Singleline)]
    private static partial Regex SectionBlock();

    [GeneratedRegex(@"<!-- settings-index -->\n.*?<!-- /settings-index -->", RegexOptions.Singleline)]
    private static partial Regex IndexBlock();

    /// <summary>The first cell of a row of keys: a line that opens with a bar and a code span. The header and the rule do not.</summary>
    [GeneratedRegex(@"^\| `(?<key>[^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex KeyCell();
}

/// <summary>
/// The words of one language of the settings reference: the titles of the columns, the names of the
/// types, the sentence that says who reads a section, and where a key's meaning comes from — the
/// catalogue's own sentence in English, a file of sentences kept by hand in French.
/// </summary>
internal sealed class SettingsReferenceLanguage
{
    private readonly SettingsReferenceSentences? _sentences;
    private readonly bool _french;

    private SettingsReferenceLanguage(bool french, SettingsReferenceSentences? sentences)
    {
        _french = french;
        _sentences = sentences;
    }

    /// <summary>English: every sentence is the catalogue's.</summary>
    public static SettingsReferenceLanguage English { get; } = new(french: false, sentences: null);

    /// <summary>French: the sentences of <paramref name="sentences"/>, one per key.</summary>
    public static SettingsReferenceLanguage French(SettingsReferenceSentences sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        return new SettingsReferenceLanguage(french: true, sentences);
    }

    public string Key => _french ? "Clé" : "Key";

    public const string Type = "Type";

    public string Default => _french ? "Défaut" : "Default";

    public string Values => _french ? "Valeurs" : "Values";

    public string Meaning => _french ? "Sens" : "Meaning";

    public string Category => _french ? "Catégorie" : "Category";

    public const string Sections = "Sections";

    public string Keys => _french ? "Clés" : "Keys";

    public const string Secret = "secret";

    /// <summary>The title of <paramref name="category"/>: its own in English, the file's in French.</summary>
    public string CategoryTitle(SettingsCategory category) =>
        _sentences is not null && _sentences.Categories.TryGetValue(category.Id, out var title) ? title : category.Title;

    /// <summary>The meaning of <paramref name="entry"/>: the catalogue's sentence, or its translation; empty when the file lacks it.</summary>
    public string Describe(SettingsCatalogEntry entry)
    {
        if (_sentences is null)
            return entry.Description;

        return _sentences.Settings.TryGetValue(entry.Path, out var sentence) ? sentence : string.Empty;
    }

    /// <summary>What the catalogue says of a default in words, or its translation; the catalogue's own when the file lacks it.</summary>
    public string DefaultNote(string note) =>
        _sentences is not null && _sentences.DefaultNotes.TryGetValue(note, out var translated) ? translated : note;

    /// <summary>A key as this language writes the names an operator chooses: <c>&lt;name&gt;</c> is <c>&lt;nom&gt;</c> in French.</summary>
    public string Placeholders(string key) =>
        _french ? key.Replace(SettingsCatalogBuilder.Name, FrenchName, StringComparison.Ordinal) : key;

    /// <summary>The reverse of <see cref="Placeholders"/>: a key of the page as the catalogue writes it.</summary>
    public string CataloguePlaceholders(string key) =>
        _french ? key.Replace(FrenchName, SettingsCatalogBuilder.Name, StringComparison.Ordinal) : key;

    private const string FrenchName = "<nom>";

    /// <summary>The name of a type of the catalogue (<c>integer</c>, <c>list of string</c>) in this language.</summary>
    public string TypeName(string type)
    {
        if (!_french)
            return type;

        const string List = "list of ";
        if (type.StartsWith(List, StringComparison.Ordinal))
        {
            return type[List.Length..] switch
            {
                "string" => "liste de chaînes",
                "integer" => "liste d'entiers",
                "number" => "liste de nombres",
                "boolean" => "liste de booléens",
                "enum" => "liste d'énumérations",
                var item => $"liste de {TypeName(item)}",
            };
        }

        return type switch
        {
            "string" => "chaîne",
            "integer" => "entier",
            "number" => "nombre",
            "boolean" => "booléen",
            "duration" => "durée",
            "uri" => "URI",
            "date-time" => "date et heure",
            "enum" => "énumération",
            "any" => "libre",
            _ => type,
        };
    }

    /// <summary>
    /// The line above a section's table: the shipped binaries that read it, or — when none does —
    /// the registration a host written in C# reads it through.
    /// </summary>
    public string ReadBy(SettingsCatalogSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.Hosts.Count > 0)
        {
            var hosts = string.Join(", ", section.Hosts.Select(host => $"`{host}`"));
            return _french ? $"**Lue par** : {hosts}." : $"**Read by**: {hosts}.";
        }

        var registration = section.Registration is null ? null : $"`{section.Registration}()`";
        if (_french)
        {
            return registration is null
                ? "**Lue par** : un hôte écrit en C# seulement — aucun binaire livré ne lit cette section."
                : $"**Lue par** : un hôte écrit en C# seulement, par {registration} — aucun binaire livré ne lit cette section.";
        }

        return registration is null
            ? "**Read by**: a host written in C# only — no shipped binary reads this section."
            : $"**Read by**: a host written in C# only, through {registration} — no shipped binary reads this section.";
    }

    /// <summary>The sentence under the index: how many sections and keys, and how many sections no shipped binary reads.</summary>
    public string Totals(int sections, int keys, int libraryOnly)
    {
        if (_french)
        {
            return libraryOnly == 0
                ? $"{sections} sections, {keys} clés."
                : $"{sections} sections, {keys} clés. Lues par aucun binaire livré, seulement par un hôte écrit en C# ({libraryOnly}) :";
        }

        return libraryOnly == 0
            ? $"{sections} sections, {keys} keys."
            : $"{sections} sections, {keys} keys. Read by no shipped binary, only by a host written in C# ({libraryOnly}):";
    }
}

/// <summary>
/// The sentences of the settings reference in a language other than the code's: one per key of the
/// catalogue, the title of each category, and each thing the catalogue says of a default in words.
/// Kept by hand in one file — <see cref="FrenchFile"/> —, which a test holds to the catalogue: a key
/// without a sentence fails it, and so does a sentence without a key.
/// </summary>
internal sealed class SettingsReferenceSentences
{
    /// <summary>The French sentences, from the repository's root: beside the page they are printed in.</summary>
    public const string FrenchFile = "docs/fr/reference/configuration.settings.json";

    private SettingsReferenceSentences(
        Dictionary<string, string> categories,
        Dictionary<string, string> defaultNotes,
        Dictionary<string, string> settings)
    {
        Categories = categories;
        DefaultNotes = defaultNotes;
        Settings = settings;
    }

    /// <summary>The title of each category, by its id.</summary>
    public IReadOnlyDictionary<string, string> Categories { get; }

    /// <summary>What stands for a default in words, by the catalogue's own wording.</summary>
    public IReadOnlyDictionary<string, string> DefaultNotes { get; }

    /// <summary>The meaning of each key, by its path as the catalogue writes it.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; }

    /// <summary>Reads the file: an object with <c>categories</c>, <c>defaultNotes</c> and <c>settings</c>, each a map of texts.</summary>
    /// <exception cref="JsonException">The text is not such a file.</exception>
    public static SettingsReferenceSentences FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return new SettingsReferenceSentences(
            Map(document.RootElement, "categories", StringComparer.Ordinal),
            Map(document.RootElement, "defaultNotes", StringComparer.Ordinal),
            Map(document.RootElement, "settings", StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What keeps these sentences from covering <paramref name="catalog"/>, one line each: a key, a
    /// category or a default's note without its sentence, an empty sentence, a sentence for a key
    /// the catalogue does not hold.
    /// </summary>
    public IReadOnlyList<string> Problems(SettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var problems = new List<string>();

        var keys = catalog.Settings.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        problems.AddRange(catalog.Settings
            .Where(entry => !Settings.TryGetValue(entry.Path, out var sentence) || string.IsNullOrWhiteSpace(sentence))
            .Select(entry => $"settings: no sentence for '{entry.Path}' (\"{entry.Description}\")."));
        problems.AddRange(Settings.Keys.Where(key => !keys.Contains(key))
            .Select(key => $"settings: '{key}' is no key of the catalogue any more — remove its sentence."));

        var categories = catalog.Sections.Select(section => section.Category).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(categories.Where(id => !Categories.TryGetValue(id, out var title) || string.IsNullOrWhiteSpace(title))
            .Select(id => $"categories: no title for '{id}'."));
        problems.AddRange(Categories.Keys.Where(id => !categories.Contains(id))
            .Select(id => $"categories: '{id}' is no category of the catalogue."));

        var notes = catalog.Settings.Select(entry => entry.DefaultNote).OfType<string>().ToHashSet(StringComparer.Ordinal);
        problems.AddRange(notes.Where(note => !DefaultNotes.TryGetValue(note, out var translated) || string.IsNullOrWhiteSpace(translated))
            .Select(note => $"defaultNotes: no sentence for \"{note}\"."));
        problems.AddRange(DefaultNotes.Keys.Where(note => !notes.Contains(note))
            .Select(note => $"defaultNotes: \"{note}\" is said of no default any more — remove its sentence."));

        return problems;
    }

    private static Dictionary<string, string> Map(JsonElement root, string field, StringComparer comparer)
    {
        var map = new Dictionary<string, string>(comparer);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                map[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return map;
    }
}

/// <summary>The pages the settings reference is printed in, from the repository's root, and how their tables are written again.</summary>
internal static class SettingsReferencePages
{
    /// <summary>The reference, in English.</summary>
    public const string English = "docs/reference/configuration.md";

    /// <summary>Its French mirror.</summary>
    public const string French = "docs/fr/reference/configuration.md";

    /// <summary>How the tables of the two pages are produced again from the catalogue.</summary>
    public const string Regenerate =
        "UPDATE_PRODUCED_FILES=1 dotnet test tests/hosting/Orkeon.Hosting.Tests --filter \"FullyQualifiedName~SettingsReferencePage\" " +
        "(after the settings catalogue itself, when a key, a default or a comment changed).";
}
