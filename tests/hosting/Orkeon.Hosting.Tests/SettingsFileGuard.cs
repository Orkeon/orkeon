using System.Globalization;
using System.Text.Json;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// What a settings text writes that the settings catalogue does not hold: a key no section carries,
/// a value its key cannot take, a key that was retired. It is the start validation's list applied to
/// a text nobody starts — a file of the repository an operator is handed to copy, a block of the
/// documentation —, judged against the <b>complete</b> catalogue: a file may be the daemon's, or a
/// host's written in C#.
/// <para>
/// It builds no host and binds nothing: it knows a key by its path — a name the operator chooses
/// where the catalogue writes <c>&lt;name&gt;</c>, an index where it writes <c>&lt;i&gt;</c> — and a
/// value by the type the catalogue declares.
/// </para>
/// </summary>
internal sealed class SettingsFileGuard
{
    private const string ListOf = "list of ";

    private static readonly JsonDocumentOptions s_lenient = new()
    {
        // What the settings readers take: comments, and a comma after the last member.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Node _root = new();

    public SettingsFileGuard(SettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var entry in catalog.Settings)
        {
            var node = _root;
            foreach (var key in entry.Path.Split(':'))
                node = node.Below(key);
            node.Entry = entry;
        }
    }

    /// <summary>The guard of everything Orkeon reads.</summary>
    public static SettingsFileGuard Complete { get; } = new(SettingsCatalog.Complete);

    /// <summary>The names the catalogue knows at the root of a settings file: <c>Llm</c>, <c>Orkeon</c>, <c>MCP</c>…</summary>
    public IReadOnlyCollection<string> RootNames => _root.Named.Keys;

    /// <summary>
    /// <paramref name="text"/> as the object a settings reader would see — comments and trailing
    /// commas taken —, or null when it is no JSON object. A fragment that opens on a member
    /// (<c>"RateLimiting": { … }</c>), as a page quotes one, is read as the object holding it.
    /// </summary>
    public static JsonDocument? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text) ?? (text.TrimStart().StartsWith('"') ? Parse("{" + text + "}") : null);
    }

    /// <summary>Whether <paramref name="settings"/> writes at its root, as the catalogue spells it, a section Orkeon reads.</summary>
    public bool WritesAKnownSection(JsonElement settings) =>
        settings.ValueKind == JsonValueKind.Object
        && settings.EnumerateObject().Any(member => _root.Named.Keys.Contains(member.Name, StringComparer.Ordinal));

    /// <summary>
    /// What <paramref name="settings"/> writes that the catalogue refuses, one sentence each naming
    /// its key; empty when every key is known and every value fits.
    /// </summary>
    /// <param name="settings">The root object of a settings text.</param>
    /// <param name="openRoot">
    /// Whether a root member the catalogue does not know is left alone — a block of a page that shows
    /// a section beside something else — or reported: a file handed to an operator writes only what
    /// Orkeon reads, a note to its reader (<c>_comment</c>, <c>$schema</c>) aside.
    /// </param>
    public IReadOnlyList<string> Problems(JsonElement settings, bool openRoot)
    {
        var problems = new List<string>();
        if (settings.ValueKind != JsonValueKind.Object)
        {
            problems.Add("The text is not a JSON object.");
            return problems;
        }

        foreach (var member in settings.EnumerateObject())
        {
            if (_root.Child(member.Name) is { } below)
                Walk(member.Value, below, member.Name, problems);
            else if (SettingsValidation.RetiredKeys.TryGetValue(member.Name, out var migration))
                problems.Add(migration);
            else if (!openRoot && !IsNote(member.Name))
                problems.Add(Unknown(parent: null, member.Name, _root));
        }

        return problems;
    }

    /// <summary>A root member that says something to whoever reads the file, and nothing to a host.</summary>
    private static bool IsNote(string name) => name.StartsWith('_') || name.StartsWith('$');

    private static JsonDocument? Parse(string text)
    {
        try
        {
            var document = JsonDocument.Parse(text, s_lenient);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Walk(JsonElement value, Node node, string path, List<string> problems)
    {
        if (SettingsValidation.RetiredKeys.TryGetValue(path, out var migration))
        {
            problems.Add(migration);
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return;

            case JsonValueKind.Object:
                if (node.Entry is { } single && !node.TakesKeys)
                {
                    // The binder reads a list written as { "0": …, "1": … }; nothing else takes keys.
                    if (!IsAny(single) && !(IsList(single) && value.EnumerateObject().All(member => IsIndex(member.Name))))
                        problems.Add($"{path} is {Article(single.Type)} {single.Type}, written as a section.");
                    else if (IsList(single))
                        problems.AddRange(value.EnumerateObject().SelectMany(member => Value(single, member.Value, $"{path}:{member.Name}", item: true)));
                    return;
                }

                foreach (var member in value.EnumerateObject())
                {
                    var below = $"{path}:{member.Name}";
                    if (node.Child(member.Name) is { } child)
                        Walk(member.Value, child, below, problems);
                    else if (SettingsValidation.RetiredKeys.TryGetValue(below, out var retired))
                        problems.Add(retired);
                    else
                        problems.Add(Unknown(path, member.Name, node));
                }

                return;

            case JsonValueKind.Array:
                if (node.Entry is { } list && !node.TakesKeys)
                {
                    if (IsAny(list))
                        return;
                    if (!IsList(list))
                        problems.Add($"{path} is {Article(list.Type)} {list.Type}, written as a list.");
                    else
                        problems.AddRange(value.EnumerateArray().SelectMany((item, index) => Value(list, item, $"{path}:{index}", item: true)));
                    return;
                }

                var position = 0;
                foreach (var item in value.EnumerateArray())
                {
                    var key = position++.ToString(CultureInfo.InvariantCulture);
                    if (node.Child(key) is { } child)
                        Walk(item, child, $"{path}:{key}", problems);
                    else
                        problems.Add($"{path} is not a list: it carries {string.Join(", ", node.Named.Keys.Order(StringComparer.OrdinalIgnoreCase))}.");
                }

                return;

            default:
                if (node.Entry is { } entry)
                    problems.AddRange(Value(entry, value, path, item: false));
                else if (Text(value).Length > 0)
                    problems.Add($"{path} is a section, written as a single value: it carries {string.Join(", ", node.Keys())}.");
                return;
        }
    }

    /// <summary>Whether <paramref name="value"/> is one the key of <paramref name="entry"/> can take, as the binder converts it.</summary>
    private static IEnumerable<string> Value(SettingsCatalogEntry entry, JsonElement value, string path, bool item)
    {
        var type = entry.Type;
        if (item)
        {
            if (!type.StartsWith(ListOf, StringComparison.Ordinal))
                yield break;
            type = type[ListOf.Length..];
        }
        else if (type.StartsWith(ListOf, StringComparison.Ordinal))
        {
            // A single value where a list is read binds nothing, without a word.
            if (Text(value).Length > 0)
                yield return $"{path} is a {type}, written as a single value: write [ … ].";
            yield break;
        }

        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            if (!string.Equals(type, SettingsCatalogBuilder.Any, StringComparison.Ordinal))
                yield return $"{path} is {Article(type)} {type}, written as a section.";
            yield break;
        }

        var text = Text(value);
        // An empty value is no value: the binder leaves the default.
        if (text.Length == 0 || Fits(type, text, entry.Values))
            yield break;

        yield return entry.Values.Count > 0
            ? $"{path} is '{text}', which is none of {string.Join(", ", entry.Values)}."
            : $"{path} is '{text}', which is not {Article(type)} {type}.";
    }

    private static bool Fits(string type, string text, IReadOnlyList<string> values) => type switch
    {
        "integer" => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "number" => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number),
        "boolean" => bool.TryParse(text, out _),
        "duration" => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out _),
        "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "uri" => Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out _),
        // An enumeration takes a name, any case, several of them for flags ("Read, Draft"), or its number.
        "enum" => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } names
            && Array.TrueForAll(names, name => values.Contains(name, StringComparer.OrdinalIgnoreCase)),
        _ => true,
    };

    private static string Unknown(string? parent, string name, Node node)
    {
        var known = node.Keys();
        var closest = SettingsValidation.Closest(name, node.Named.Keys);
        if (parent is null)
        {
            return $"{name} is no section of the settings catalogue."
                + (closest is null ? string.Empty : $" Did you mean {closest}?");
        }

        var listed = SettingsCatalog.Complete.SectionCovering(parent) is { } section
            ? $" `orkeon settings {section.Path}` lists its keys."
            : string.Empty;
        return $"{parent}:{name} is not a setting: {parent} carries {string.Join(", ", known)}."
            + (closest is null ? string.Empty : $" Did you mean {parent}:{closest}?")
            + listed;
    }

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    private static bool IsAny(SettingsCatalogEntry entry) =>
        string.Equals(entry.Type, SettingsCatalogBuilder.Any, StringComparison.Ordinal);

    private static bool IsList(SettingsCatalogEntry entry) => entry.Type.StartsWith(ListOf, StringComparison.Ordinal);

    private static bool IsIndex(string key) =>
        int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static string Article(string type) => type[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a";

    /// <summary>One step of a key's path: the keys below it by name, under any name, under any index — and the key it is, when it is one.</summary>
    private sealed class Node
    {
        public Dictionary<string, Node> Named { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Node? AnyName { get; private set; }

        public Node? AnyIndex { get; private set; }

        public SettingsCatalogEntry? Entry { get; set; }

        public bool TakesKeys => Named.Count > 0 || AnyName is not null || AnyIndex is not null;

        public Node Below(string key)
        {
            if (string.Equals(key, SettingsCatalogBuilder.Name, StringComparison.Ordinal))
                return AnyName ??= new Node();
            if (string.Equals(key, SettingsCatalogBuilder.Index, StringComparison.Ordinal))
                return AnyIndex ??= new Node();
            if (!Named.TryGetValue(key, out var node))
                Named[key] = node = new Node();
            return node;
        }

        public Node? Child(string key) =>
            Named.TryGetValue(key, out var named) ? named
            : AnyIndex is not null && IsIndex(key) ? AnyIndex
            : AnyName;

        /// <summary>What the node carries, as a refusal lists it: its keys, and the placeholder of a free name or index.</summary>
        public IReadOnlyList<string> Keys() =>
        [
            .. Named.Keys.Order(StringComparer.OrdinalIgnoreCase),
            .. AnyName is null ? Array.Empty<string>() : [SettingsCatalogBuilder.Name],
            .. AnyIndex is null ? Array.Empty<string>() : [SettingsCatalogBuilder.Index],
        ];
    }
}
