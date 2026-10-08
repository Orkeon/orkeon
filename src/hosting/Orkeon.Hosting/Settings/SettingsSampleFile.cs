using System.Text;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.Configuration;

namespace Orkeon.Hosting;

/// <summary>
/// The sample settings file an installation carries (<c>appsettings.sample.json</c>): every key a
/// shipped binary reads, by category, produced from the <see cref="SettingsCatalog"/> and never
/// written by hand. It is a file to read and a file that can be copied: put in place of the settings
/// file as it is, it changes nothing.
/// <para>
/// That is why it writes one kind of key only — a single value at its default, which the reader
/// reads back as what it already had. Everything else is shown as a comment, where its reader finds
/// nothing: a key without a default, a secret, a list (the configuration binder adds what a file
/// writes to the entries a list already holds), an entry under a name or an index the operator
/// chooses, a key whose default a profile sets, and the whole of a section whose mere presence
/// decides something (<see cref="ShownOnly"/>). An object that would hold comments only is a
/// comment itself: an empty object is a key without a value, not an absent section.
/// </para>
/// <para>
/// The file is JSON with <c>//</c> comments and a comma after every member, which the three readers
/// of a settings file take — .NET's JSON configuration, the runner's own reading of the mounts and
/// Orkeon Studio — and which let a line be uncommented without touching its neighbours.
/// </para>
/// </summary>
internal static class SettingsSampleFile
{
    /// <summary>Where the produced file is committed, from the repository's root.</summary>
    public const string RepositoryPath = "scripts/installer-assets/appsettings.sample.json";

    /// <summary>How the file is written again.</summary>
    public const string Regenerate =
        "UPDATE_PRODUCED_FILES=1 dotnet test --filter \"FullyQualifiedName~SettingsSampleFile\" on tests/hosting/Orkeon.Hosting.Tests";

    private const int Width = 100;

    private const string Indent = "  ";

    /// <summary>
    /// The sections shown and never written, each with the reason the file gives: one key of
    /// <c>Llm</c> written, even at its default, and the host has a default model — it stops saying
    /// that none is configured and stops running on the echo provider.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ShownOnly { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConfigurationKeys.LlmSection] =
                "Shown, not written: one key of this section written and the host has a default model. Left " +
                "as it is, a host without a model says so and runs on the echo provider; `orkeon init` writes the section.",
        };

    /// <summary>The file for <paramref name="catalog"/>: the sections at least one shipped binary reads.</summary>
    /// <param name="catalog">The complete catalogue; the sections no shipped binary reads are left out.</param>
    public static string Produce(SettingsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var shipped = catalog.ForShippedHosts();
        var root = new Node(string.Empty, string.Empty);
        foreach (var section in shipped.Sections)
        {
            root.At(section.Path).Section = section;
            foreach (var entry in shipped.SettingsOf(section.Path))
                root.At(entry.Path).Entry = entry;
        }

        var text = new StringBuilder();
        foreach (var line in Header())
            text.Append(line).Append('\n');
        text.Append("{\n");
        Members(text, root, depth: 1, shown: false);
        text.Append("}\n");
        return text.ToString();
    }

    /// <summary>
    /// Whether the file writes <paramref name="entry"/> — a single value at a default that is a
    /// constant —, rather than showing it as a comment.
    /// </summary>
    public static bool Writes(SettingsCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Default is not null
               && entry.DefaultNote is null
               && !entry.Secret
               && entry.Type != SettingsCatalogBuilder.Any
               && !entry.Type.StartsWith("list of ", StringComparison.Ordinal)
               && !entry.Path.Contains(SettingsCatalogBuilder.Name, StringComparison.Ordinal)
               && !entry.Path.Contains(SettingsCatalogBuilder.Index, StringComparison.Ordinal)
               && !ShownOnly.Keys.Any(section => IsAtOrBelow(entry.Path, section));
    }

    private static IEnumerable<string> Header()
    {
        string[] paragraphs =
        [
            "Orkeon settings: every key a shipped binary reads — orkeon, orkeon-host, orkeon-repl —, at its default, by category.",
            "A file to read: no binary loads it. The settings file is the one `orkeon init` writes and `orkeon doctor` " +
            "names on its `appsettings` line; `--settings <path>` names another.",
            "Copied in its place as it is, this file changes nothing: each key it writes holds the value that key has " +
            "when it is left out. What cannot be written that way is shown as a comment —",
        ];
        string[] shown =
        [
            "a key without a default:   // \"Model\": <string>,",
            "a secret, which belongs in an environment variable:   // \"ApiKey\": <secret>,",
            "a list, with the entries it holds when it is left out: written, a list adds to them;",
            "an entry under a name or an index you choose:   // \"<name>\": { … },",
            "a key a RAG profile sets: written, it would pin the key for every profile;",
            "the `Llm` section, for the reason given above it.",
        ];
        string[] closing =
        [
            "Uncomment a line to set it. `orkeon settings <section>` describes each key — type, default, meaning — and " +
            "`orkeon settings` lists the sections by category.",
        ];

        foreach (var paragraph in paragraphs)
        {
            foreach (var line in Wrapped(paragraph, Width - 3))
                yield return "// " + line;
            if (!ReferenceEquals(paragraph, paragraphs[^1]))
                yield return "//";
        }

        foreach (var item in shown)
            yield return "//   - " + item;
        yield return "//";
        foreach (var line in closing.SelectMany(paragraph => Wrapped(paragraph, Width - 3)))
            yield return "// " + line;
    }

    /// <summary>The members of <paramref name="parent"/>, one per line, each ending on a comma.</summary>
    private static void Members(StringBuilder text, Node parent, int depth, bool shown)
    {
        var first = true;
        foreach (var node in parent.Children)
        {
            var prefix = Prefix(depth, shown || !node.Written);
            // A section, and a name of the root that groups several, start after an empty line.
            if (!first && (node.Section is not null || depth == 1))
                text.Append('\n');
            if (node.Section is { } section)
                SectionHeader(text, section, Prefix(depth, shown: true));

            first = false;
            if (node.Entry is { } entry)
            {
                text.Append(prefix).Append(Member(node.Name, entry)).Append('\n');
                continue;
            }

            // A list of sections: one item, under the index the catalogue writes <i>.
            if (node.Children is [{ Name: SettingsCatalogBuilder.Index } item])
            {
                text.Append(prefix).Append(Quoted(node.Name)).Append(": [\n");
                text.Append(Prefix(depth + 1, shown: true)).Append("{\n");
                Members(text, item, depth + 2, shown: true);
                text.Append(Prefix(depth + 1, shown: true)).Append("},\n");
                text.Append(prefix).Append("],\n");
                continue;
            }

            text.Append(prefix).Append(Quoted(node.Name)).Append(": {\n");
            Members(text, node, depth + 1, shown || !node.Written);
            text.Append(prefix).Append("},\n");
        }
    }

    private static void SectionHeader(StringBuilder text, SettingsCatalogSection section, string prefix)
    {
        var category = SettingsCategories.All.FirstOrDefault(known => string.Equals(known.Id, section.Category, StringComparison.Ordinal))?.Title
                       ?? section.Category;
        var heading = $"{section.Path} — {category} — read by {string.Join(", ", section.Hosts)}.";
        var sentence = FirstSentence(section.Description);
        var reason = ShownOnly.GetValueOrDefault(section.Path);
        foreach (var paragraph in new[] { heading, sentence, reason })
        {
            if (string.IsNullOrEmpty(paragraph))
                continue;

            foreach (var line in Wrapped(paragraph, Math.Max(40, Width - prefix.Length)))
                text.Append(prefix).Append(line).Append('\n');
        }
    }

    /// <summary>One member: written with its default, or shown with what stands for the value it has not.</summary>
    private static string Member(string name, SettingsCatalogEntry entry)
    {
        var values = entry.Values.Count > 0 ? string.Join(" | ", entry.Values) : null;
        string value;
        string? remark = null;
        if (entry.Secret)
        {
            value = "<secret>";
        }
        else if (entry.Default is { } literal)
        {
            value = literal;
            remark = entry.DefaultNote ?? values;
        }
        else if (entry.DefaultNote is { } note)
        {
            // A dictionary's first entries are named; a value the machine or the moment gives is said.
            value = name == SettingsCatalogBuilder.Name ? $"<{entry.Type}>" : $"<{entry.Type}: {note}>";
            remark = name == SettingsCatalogBuilder.Name ? note : null;
        }
        else
        {
            value = values is null ? $"<{entry.Type}>" : $"<{values}>";
        }

        return $"{Quoted(name)}: {value}," + (remark is null ? string.Empty : $"  // {remark}");
    }

    private static string Quoted(string name) => $"\"{name}\"";

    private static string Prefix(int depth, bool shown)
    {
        var indent = string.Concat(Enumerable.Repeat(Indent, depth));
        return shown ? indent + "// " : indent;
    }

    /// <summary>The first sentence of <paramref name="text"/>: up to a full stop a capital, a backtick or the end follows.</summary>
    private static string FirstSentence(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '.')
                continue;
            if (index == text.Length - 1)
                return text;
            if (text[index + 1] == ' ' && index + 2 < text.Length && (char.IsUpper(text[index + 2]) || text[index + 2] == '`'))
                return text[..(index + 1)];
        }

        return text;
    }

    private static IEnumerable<string> Wrapped(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            yield return line.ToString();
    }

    private static bool IsAtOrBelow(string path, string section) =>
        string.Equals(path, section, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(section + ConfigurationPath.KeyDelimiter, StringComparison.OrdinalIgnoreCase);

    /// <summary>One name of the file's tree: an object, or a key when the catalogue has an entry at its path.</summary>
    private sealed class Node(string name, string path)
    {
        public string Name { get; } = name;

        public string Path { get; } = path;

        public List<Node> Children { get; } = [];

        public SettingsCatalogSection? Section { get; set; }

        public SettingsCatalogEntry? Entry { get; set; }

        /// <summary>Whether the file writes this name: a key it writes, or an object holding one.</summary>
        public bool Written => Entry is { } entry ? Writes(entry) : Children.Exists(child => child.Written);

        /// <summary>The node at <paramref name="path"/> below this one, created with those above it, in the order they are first asked for.</summary>
        public Node At(string path)
        {
            var node = this;
            foreach (var key in path.Split(ConfigurationPath.KeyDelimiter))
            {
                var next = node.Children.Find(child => string.Equals(child.Name, key, StringComparison.OrdinalIgnoreCase));
                if (next is null)
                {
                    next = new Node(key, node.Path.Length == 0 ? key : node.Path + ConfigurationPath.KeyDelimiter + key);
                    node.Children.Add(next);
                }

                node = next;
            }

            return node;
        }
    }
}
