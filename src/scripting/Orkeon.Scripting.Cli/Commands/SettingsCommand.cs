using System.Text;
using System.Text.Json;
using Orkeon.Constants.Configuration;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>What one <c>orkeon settings …</c> command line asks.</summary>
internal sealed record SettingsCommandOptions
{
    /// <summary>The category, section, key or word asked about, or null for the categories.</summary>
    public string? Name { get; init; }

    /// <summary><c>--all</c>: every section and every key, by category.</summary>
    public bool All { get; init; }

    /// <summary><c>--json</c>: the catalogue itself instead of the listing.</summary>
    public bool Json { get; init; }

    /// <summary><c>--host</c>: the binary whose settings are listed — a <see cref="SettingsHosts"/> name —, or null for all.</summary>
    public string? Host { get; init; }

    /// <summary><c>--help</c>.</summary>
    public bool Help { get; init; }

    /// <summary>Why the command line is refused, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The spellings <c>--host</c> accepts: the short word, and the name of the binary.</summary>
    private static readonly Dictionary<string, string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["run"] = SettingsHosts.Cli,
        [SettingsHosts.Cli] = SettingsHosts.Cli,
        ["host"] = SettingsHosts.ServiceHost,
        [SettingsHosts.ServiceHost] = SettingsHosts.ServiceHost,
        ["repl"] = SettingsHosts.Repl,
        [SettingsHosts.Repl] = SettingsHosts.Repl,
    };

    /// <summary>Reads the arguments that follow the verb.</summary>
    public static SettingsCommandOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        const string HostOption = "--host";
        var words = new List<string>();
        var options = new SettingsCommandOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? host = null;
            switch (arg)
            {
                case "--help" or "-h":
                    return new SettingsCommandOptions { Help = true };
                case "--all":
                    options = options with { All = true };
                    continue;
                case "--json":
                    options = options with { Json = true };
                    continue;
                case HostOption when i + 1 < args.Length && !args[i + 1].StartsWith('-'):
                    host = args[++i];
                    break;
                case HostOption:
                    return Refused("--host needs a binary: run, host or repl.");
                default:
                    if (arg.StartsWith(HostOption + "=", StringComparison.Ordinal))
                        host = arg[(HostOption.Length + 1)..];
                    else if (arg.StartsWith('-'))
                        return Refused($"unknown option '{arg}'; run `orkeon settings --help`.");
                    else
                        words.Add(arg);
                    break;
            }

            if (host is null)
                continue;
            if (!Hosts.TryGetValue(host, out var binary))
                return Refused($"--host takes run, host or repl, not '{host}'.");
            options = options with { Host = binary };
        }

        var name = string.Join(' ', words).Trim();
        if (options.All && name.Length > 0 && !options.Json)
            return Refused($"--all lists everything; drop it to look '{name}' up.");

        return options with { Name = name.Length == 0 ? null : name };
    }

    private static SettingsCommandOptions Refused(string error) => new() { Error = error };
}

/// <summary>
/// <c>orkeon settings</c> — the settings a host reads, in the terminal: the categories, then a
/// category, a section, a key, or whatever a word names or describes; <c>--all</c> for everything
/// and <c>--json</c> for the catalogue itself. <c>orkeon settings env</c> lists the environment
/// variables Orkeon reads (<see cref="EnvironmentVariableNames"/>).
/// </summary>
/// <remarks>
/// It shows what a settings file may hold and what each key is worth when nothing sets it — not
/// what this machine has set: it reads the catalogue the tool carries
/// (<see cref="SettingsCatalog.Complete"/>) and nothing else. No settings file, no host, no
/// network: it answers the same where the file is refused, and it has no secret to print.
/// <c>orkeon doctor</c> stays the verb that judges a file.
/// </remarks>
internal static class SettingsCommand
{
    /// <summary>The widest line the listing writes.</summary>
    internal const int Width = 100;

    private const string Prefix = "orkeon settings: ";

    /// <summary>The word that asks for the environment variables instead of a setting.</summary>
    private const string EnvironmentForm = "env";

    /// <summary>The forms of the verb, shown by <c>--help</c> and under the categories.</summary>
    private static readonly string Forms = string.Join(
        Environment.NewLine,
        "  orkeon settings                   the categories, and the sections of each",
        "  orkeon settings <category>        the sections of a category, and who reads each",
        "  orkeon settings <section>         the keys of a section: type, default, meaning",
        "  orkeon settings <key>             one key",
        "  orkeon settings <word>            whatever is named or described with the word",
        "  orkeon settings env               the environment variables Orkeon reads",
        "  orkeon settings --all             every section and every key, by category",
        "  orkeon settings --json            the same inventory for a program; follows a name and --host",
        "  orkeon settings --host <binary>   only what one binary reads: run, host or repl",
        string.Empty,
        "  For instance: orkeon settings rate · orkeon settings RateLimiting · orkeon settings --host repl");

    /// <summary>Dispatches <c>orkeon settings …</c> (arguments already stripped of the verb).</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        var options = SettingsCommandOptions.Parse(args);
        if (options.Help)
        {
            await Console.Out.WriteAsync(Usage()).ConfigureAwait(false);
            return Program.ExitOk;
        }

        if (options.Error is not null)
            return await RefuseAsync(options.Error).ConfigureAwait(false);

        if (string.Equals(options.Name, EnvironmentForm, StringComparison.OrdinalIgnoreCase))
        {
            if (options.All || options.Host is not null)
            {
                return await RefuseAsync(
                    "`orkeon settings env` takes --json and nothing else: each variable says who reads it.").ConfigureAwait(false);
            }

            await Console.Out.WriteAsync(options.Json ? EnvironmentJson() : EnvironmentVariables()).ConfigureAwait(false);
            return Program.ExitOk;
        }

        var catalog = options.Host is null ? SettingsCatalog.Complete : SettingsCatalog.Complete.ForHost(options.Host);
        var selection = options.Name is null ? Selection.Everything(catalog) : Selection.Exact(catalog, options.Name);
        if (selection is null)
        {
            // A name another binary answers to is said so, before the word is looked for in what this one reads.
            var name = options.Name!;
            if (options.Host is not null && Selection.Exact(SettingsCatalog.Complete, name) is { } elsewhere)
                return await RefuseAsync(NotReadBy(elsewhere, options.Host)).ConfigureAwait(false);

            selection = Selection.Word(catalog, name);
            if (selection is null)
                return await RefuseAsync(NotFound(name, options.Host)).ConfigureAwait(false);
        }

        var text = options.Json ? selection.Narrowed.ToJson()
            : selection.Kind == SelectionKind.Everything && !options.All ? Categories(catalog, options.Host)
            : Render(selection, catalog, options.Host);
        await Console.Out.WriteAsync(text).ConfigureAwait(false);
        return Program.ExitOk;
    }

    private static async Task<int> RefuseAsync(string reason)
    {
        await Console.Error.WriteLineAsync(Prefix + reason).ConfigureAwait(false);
        return Program.ExitScriptError;
    }

    private static string Usage()
    {
        var page = new Page();
        page.Line("orkeon settings — the settings a host reads, by category: type, default, meaning, who reads it.");
        page.Line();
        page.Line(Forms);
        page.Line();
        page.Wrapped(
            "It lists what a settings file may hold and the value of each key when nothing sets it, not what " +
            "this machine has set: it opens no file. `orkeon doctor` judges a settings file.",
            indent: 0);
        page.Line();
        page.Line("Reference: " + CliUsage.SettingsReference);
        return page.ToString();
    }

    // ── the environment variables ──

    /// <summary>The settings keys that hold the name of a variable, as the catalogue writes them.</summary>
    private static List<string> KeysNamingAVariable() =>
    [
        .. SettingsCatalog.Complete.Settings
            .Select(setting => setting.Path)
            .Where(path => EnvironmentVariableNames.NamingKeySuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal))),
    ];

    /// <summary>
    /// The three kinds of environment variables Orkeon reads: the ones that carry a setting, the
    /// ones a binary reads by their name, the ones a setting names — then what the word
    /// <c>env</c> names among the settings, which this form would otherwise hide.
    /// </summary>
    private static string EnvironmentVariables()
    {
        const string Prefixed = EnvironmentVariableNames.SettingsPrefix;
        var byName = EnvironmentVariableNames.ReadByName;
        var page = new Page();
        page.Wrapped(
            $"Environment variables Orkeon reads: any setting by its path, {byName.Count} by their name, and those a setting names.",
            indent: 0);

        page.Line();
        page.Line("A setting, by its path");
        page.Line($"  {Prefixed}<Section>__<Key>");
        page.Wrapped(
            $"Any setting, over the settings file, `__` between the levels of its path: {Prefixed}Llm__Model is " +
            $"Llm:Model, {Prefixed}RateLimiting__MaxConcurrentRequests is RateLimiting:MaxConcurrentRequests, " +
            $"{Prefixed}Orkeon__Rag__Profile is Orkeon:Rag:Profile.",
            indent: 6);
        page.Line("  <Section>__<Key>");
        page.Wrapped("The same setting without the prefix, under the settings file: Llm__Model.", indent: 6);
        page.Line($"  {Prefixed}<NAME>");
        page.Wrapped(
            $"A secret by its name, before the `Secrets` section of the file: {Prefixed}TAVILY_API_KEY is the key of `web_search`.",
            indent: 6);

        page.Line();
        page.Line("A variable a binary reads by its name");
        var nameWidth = byName.Max(entry => entry.Name.Length);
        foreach (var entry in byName)
        {
            page.Line($"  {entry.Name.PadRight(nameWidth)}  {entry.Value}");
            page.Wrapped("Read by: " + string.Join(", ", entry.ReadBy), indent: 6);
            page.Wrapped(entry.Effect, indent: 6);
        }

        page.Line();
        page.Line("A variable a setting names: the key holds the name of the variable, never its value");
        foreach (var key in KeysNamingAVariable())
            page.Line("  " + key);
        page.Line();
        page.Line("  `orkeon settings <key>` describes each of these keys.");

        var catalog = SettingsCatalog.Complete;
        if (Selection.Word(catalog, EnvironmentForm) is { } found)
        {
            page.Line();
            Found(page, catalog, found);
        }

        page.Line();
        page.Line("Reference: " + CliUsage.SettingsReference + "#environment-variables");
        return page.ToString();
    }

    /// <summary>The same table for a program: stable, in the order of the listing.</summary>
    private static string EnvironmentJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, SettingsCatalog.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("settingsPrefix", EnvironmentVariableNames.SettingsPrefix);
            writer.WriteStartArray("variables");
            foreach (var entry in EnvironmentVariableNames.ReadByName)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteStartArray("readBy");
                foreach (var binary in entry.ReadBy)
                    writer.WriteStringValue(binary);
                writer.WriteEndArray();
                writer.WriteString("value", entry.Value);
                writer.WriteString("effect", entry.Effect);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("namedBySettings");
            foreach (var key in KeysNamingAVariable())
                writer.WriteStringValue(key);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    // ── what a name designates ──

    private enum SelectionKind
    {
        Everything,
        Category,
        Section,
        Key,
        Word,
    }

    /// <summary>What a name designates in a catalogue, and that part of the catalogue.</summary>
    private sealed class Selection
    {
        public required SelectionKind Kind { get; init; }

        /// <summary>The name as it was asked: the word a search looked for.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>The category, when the name is one.</summary>
        public SettingsCategory? Category { get; init; }

        /// <summary>The sections designated: the category's, the section, the key's, or those a word names.</summary>
        public IReadOnlyList<SettingsCatalogSection> Sections { get; init; } = [];

        /// <summary>The keys designated outside whole sections: the key, or those a word names.</summary>
        public List<SettingsCatalogEntry> Keys { get; init; } = [];

        /// <summary>The sections whose description holds the word, their name not.</summary>
        public List<SettingsCatalogSection> DescribedSections { get; init; } = [];

        /// <summary>The keys whose description holds the word, their name not.</summary>
        public List<SettingsCatalogEntry> DescribedKeys { get; init; } = [];

        /// <summary>The catalogue of what is designated: what <c>--json</c> writes.</summary>
        public required SettingsCatalog Narrowed { get; init; }

        public static Selection Everything(SettingsCatalog catalog) =>
            new() { Kind = SelectionKind.Everything, Sections = catalog.Sections, Narrowed = catalog };

        /// <summary>
        /// The category <paramref name="name"/> is the id or the title of, else the section it is the
        /// path of, else the key — a name the operator chose standing where the catalogue writes
        /// <c>&lt;name&gt;</c> —, or null.
        /// </summary>
        public static Selection? Exact(SettingsCatalog catalog, string name)
        {
            var category = SettingsCategories.All.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Title, name, StringComparison.OrdinalIgnoreCase));
            if (category is not null)
            {
                var narrowed = catalog.Where(section => string.Equals(section.Category, category.Id, StringComparison.Ordinal));
                return narrowed.Sections.Count == 0
                    ? null
                    : new Selection { Kind = SelectionKind.Category, Name = name, Category = category, Sections = narrowed.Sections, Narrowed = narrowed };
            }

            if (catalog.Section(name) is { } section && !IsItsOwnKey(catalog, section))
            {
                return new Selection
                {
                    Kind = SelectionKind.Section,
                    Name = name,
                    Sections = [section],
                    Narrowed = catalog.Where(candidate => ReferenceEquals(candidate, section)),
                };
            }

            var entry = catalog.Setting(name) ?? catalog.Settings.FirstOrDefault(candidate => Names(candidate.Path, name));
            if (entry is null || catalog.Section(entry.Section) is not { } owner)
                return null;

            return new Selection
            {
                Kind = SelectionKind.Key,
                Name = name,
                Sections = [owner],
                Keys = [entry],
                Narrowed = new SettingsCatalog([owner], [entry]),
            };
        }

        /// <summary>
        /// What holds <paramref name="word"/>, any case: the sections and the keys whose path does,
        /// then those whose description does, or null when nothing does.
        /// </summary>
        public static Selection? Word(SettingsCatalog catalog, string word)
        {
            var sections = catalog.Sections.Where(section => Holds(section.Path, word)).ToList();
            var covered = sections.Select(section => section.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keys = catalog.Settings.Where(entry => !covered.Contains(entry.Section) && Holds(entry.Path, word)).ToList();
            var describedSections = catalog.Sections
                .Where(section => !covered.Contains(section.Path) && Holds(section.Description, word))
                .ToList();
            var describedKeys = catalog.Settings
                .Where(entry => !covered.Contains(entry.Section) && !Holds(entry.Path, word) && Holds(entry.Description, word))
                .ToList();
            if (sections.Count + keys.Count + describedSections.Count + describedKeys.Count == 0)
                return null;

            // For a program: the sections found whole, and the keys found with the section they are in.
            var whole = covered.Concat(describedSections.Select(section => section.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var single = keys.Concat(describedKeys).Where(entry => !whole.Contains(entry.Section)).ToList();
            var owners = single.Select(entry => entry.Section).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new Selection
            {
                Kind = SelectionKind.Word,
                Name = word,
                Sections = sections,
                Keys = keys,
                DescribedSections = describedSections,
                DescribedKeys = describedKeys,
                Narrowed = new SettingsCatalog(
                    catalog.Sections.Where(section => whole.Contains(section.Path) || owners.Contains(section.Path)),
                    catalog.Settings.Where(entry => whole.Contains(entry.Section)).Concat(single)),
            };
        }

        /// <summary>
        /// A section that is one key and nothing else (<c>BRAVE_API_KEY</c>): its name is shown as
        /// the key it is.
        /// </summary>
        private static bool IsItsOwnKey(SettingsCatalog catalog, SettingsCatalogSection section)
        {
            var keys = catalog.SettingsOf(section.Path).Take(2).ToList();
            return keys.Count == 1 && string.Equals(keys[0].Path, section.Path, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether <paramref name="name"/> is <paramref name="path"/> with a name or an index of the
        /// operator's where the catalogue writes <c>&lt;name&gt;</c> or <c>&lt;i&gt;</c>.
        /// </summary>
        private static bool Names(string path, string name)
        {
            var expected = path.Split(':');
            var given = name.Split(':');
            if (expected.Length != given.Length)
                return false;

            for (var i = 0; i < expected.Length; i++)
            {
                var free = expected[i] is SettingsCatalogBuilder.Name or SettingsCatalogBuilder.Index && given[i].Length > 0;
                if (!free && !string.Equals(expected[i], given[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a path or a description holds the word, any case, where a word of its own starts:
        /// <c>rate</c> is in <c>ToolRateLimiting</c> and in "rate limiting", not in
        /// <c>FallbackStrategy</c> nor in "generate".
        /// </summary>
        private static bool Holds(string text, string word)
        {
            for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                 at >= 0;
                 at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
            {
                if (at == 0 || !char.IsLetterOrDigit(text[at - 1]) || (char.IsUpper(text[at]) && !char.IsUpper(text[at - 1])))
                    return true;
            }

            return false;
        }
    }

    /// <summary>What to say of a name another binary answers to, and <paramref name="host"/> does not.</summary>
    private static string NotReadBy(Selection elsewhere, string host)
    {
        if (elsewhere.Kind == SelectionKind.Category)
        {
            var readers = string.Join("; ", elsewhere.Sections.Select(section => $"{section.Path}: {ReadBy(section)}"));
            return $"no section of {elsewhere.Category!.Title} is read by {host}. Read by: {readers}.";
        }

        var name = elsewhere.Kind == SelectionKind.Key ? elsewhere.Keys[0].Path : elsewhere.Sections[0].Path;
        return $"{name} is not read by {host}. Read by: {ReadBy(elsewhere.Sections[0])}.";
    }

    /// <summary>What to say of a name that designates nothing: the closest name there is.</summary>
    private static string NotFound(string name, string? host)
    {
        var where = host is null ? string.Empty : $" among what {host} reads";
        return $"nothing is named or described with '{name}'{where}. The closest name is {Closest(name)}; " +
               "`orkeon settings` lists the categories.";
    }

    /// <summary>The category, section or key whose name — whole, or its last part — is the fewest edits from <paramref name="name"/>.</summary>
    private static string Closest(string name)
    {
        var catalog = SettingsCatalog.Complete;
        var candidates = SettingsCategories.All.Select(category => category.Id)
            .Concat(catalog.Sections.Select(section => section.Path))
            .Concat(catalog.Settings.Select(entry => entry.Path));

        var wanted = name.ToUpperInvariant();
        var best = string.Empty;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var whole = candidate.ToUpperInvariant();
            var last = whole[(whole.LastIndexOf(':') + 1)..];
            var distance = Math.Min(Distance(wanted, whole), Distance(wanted, last));
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The number of insertions, deletions and substitutions between two words.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    // ── the listing ──

    /// <summary>The categories: each with its counts and the names of its sections.</summary>
    private static string Categories(SettingsCatalog catalog, string? host)
    {
        var page = new Page();
        page.Line(Total(catalog, host));
        page.Line();

        var categories = SettingsCategories.All.Where(category => SectionsOf(catalog, category).Count > 0).ToList();
        var titleWidth = categories.Max(category => category.Title.Length) + 2;
        var idWidth = categories.Max(category => category.Id.Length) + 2;
        var marked = false;
        foreach (var category in categories)
        {
            var sections = SectionsOf(catalog, category);
            var keys = sections.Sum(section => catalog.SettingsOf(section.Path).Count());
            page.Line(
                category.Title.PadRight(titleWidth) + category.Id.PadRight(idWidth) +
                Count(sections.Count, "section").PadLeft(11) + Count(keys, "key").PadLeft(10));
            page.Wrapped(
                string.Join(", ", sections.Select(section => section.Hosts.Count == 0 ? section.Path + "*" : section.Path)),
                indent: 4);
            marked |= sections.Any(section => section.Hosts.Count == 0);
        }

        page.Line();
        if (marked)
        {
            page.Line("  * read by a C# host only: no shipped binary reads it.");
            page.Line();
        }

        page.Line(Forms);
        page.Line();
        page.Line("Reference: " + CliUsage.SettingsReference);
        return page.ToString();
    }

    private static string Render(Selection selection, SettingsCatalog catalog, string? host)
    {
        var page = new Page();
        switch (selection.Kind)
        {
            case SelectionKind.Everything:
                page.Line(Total(catalog, host));
                foreach (var category in SettingsCategories.All.Where(category => SectionsOf(catalog, category).Count > 0))
                {
                    var sections = SectionsOf(catalog, category);
                    page.Line();
                    page.Line($"== {CategoryHead(catalog, category, sections)} ==");
                    foreach (var section in sections)
                    {
                        page.Line();
                        Section(page, catalog, section, [.. catalog.SettingsOf(section.Path)]);
                    }
                }

                break;

            case SelectionKind.Category:
                page.Line(CategoryHead(catalog, selection.Category!, selection.Sections));
                page.Line();
                foreach (var section in selection.Sections)
                    SectionSummary(page, catalog, section, withMeaning: true);
                page.Line();
                page.Line("  `orkeon settings <section>` lists the keys of a section.");
                break;

            case SelectionKind.Section:
                Section(page, catalog, selection.Sections[0], [.. catalog.SettingsOf(selection.Sections[0].Path)]);
                break;

            case SelectionKind.Key:
                Section(page, catalog, selection.Sections[0], selection.Keys, withMeaning: false);
                break;

            default:
                Found(page, catalog, selection);
                break;
        }

        return page.ToString();
    }

    /// <summary>What a word names, then what it only describes: sections with who reads them, keys one line each.</summary>
    private static void Found(Page page, SettingsCatalog catalog, Selection found)
    {
        var sections = found.Sections.Count + found.DescribedSections.Count;
        var keys = found.Keys.Count + found.DescribedKeys.Count;
        page.Line($"Named or described with \"{found.Name}\": {Count(sections, "section")}, {Count(keys, "key")}.");

        if (found.Sections.Count > 0)
        {
            page.Line();
            page.Line("Sections named with it");
            foreach (var section in found.Sections)
                SectionSummary(page, catalog, section, withMeaning: false);
        }

        if (found.Keys.Count > 0)
        {
            page.Line();
            page.Line("Keys named with it");
            Keys(page, found.Keys, withMeaning: false);
        }

        if (found.DescribedSections.Count > 0)
        {
            page.Line();
            page.Line("Sections described with it");
            foreach (var section in found.DescribedSections)
                SectionSummary(page, catalog, section, withMeaning: false);
        }

        if (found.DescribedKeys.Count > 0)
        {
            page.Line();
            page.Line("Keys described with it");
            Keys(page, found.DescribedKeys, withMeaning: false);
        }

        page.Line();
        page.Line("  `orkeon settings <section>` lists the keys of a section, `orkeon settings <key>` describes one.");
    }

    /// <summary>A section in full: its category, who reads it, what it configures, then <paramref name="keys"/>.</summary>
    private static void Section(
        Page page, SettingsCatalog catalog, SettingsCatalogSection section, List<SettingsCatalogEntry> keys, bool withMeaning = true)
    {
        var category = SettingsCategories.All.FirstOrDefault(candidate => string.Equals(candidate.Id, section.Category, StringComparison.Ordinal));
        page.Line(category is null ? section.Path : $"{section.Path} — {category.Title} ({category.Id})");
        page.Line("Read by: " + ReadBy(section));
        if (withMeaning && section.Description.Length > 0)
        {
            page.Line();
            page.Wrapped(section.Description, indent: 2);
        }

        page.Line();
        Keys(page, keys, withMeaning: true);
        if (!withMeaning)
        {
            var others = catalog.SettingsOf(section.Path).Count() - keys.Count;
            if (others > 0)
            {
                page.Line();
                page.Line($"  `orkeon settings {section.Path}` lists the {Count(others, "other key")} of the section.");
            }
        }
    }

    /// <summary>A section in three lines: its name and size, who reads it, the first sentence of what it configures.</summary>
    private static void SectionSummary(Page page, SettingsCatalog catalog, SettingsCatalogSection section, bool withMeaning)
    {
        page.Line($"  {section.Path} — {Count(catalog.SettingsOf(section.Path).Count(), "key")}");
        page.Line("      Read by: " + ReadBy(section));
        if (withMeaning && section.Description.Length > 0)
            page.Wrapped(FirstSentence(section.Description), indent: 6);
    }

    /// <summary>
    /// The keys, aligned: path, type, default — the default on its own lines when the three do not
    /// fit —, then, with the meaning, the values a closed list accepts and the key's sentence.
    /// </summary>
    private static void Keys(Page page, List<SettingsCatalogEntry> keys, bool withMeaning)
    {
        if (keys.Count == 0)
            return;

        var pathWidth = keys.Max(entry => entry.Path.Length);
        var typeWidth = keys.Max(entry => entry.Type.Length);
        foreach (var entry in keys)
        {
            var value = DefaultOf(entry);
            var head = $"  {entry.Path.PadRight(pathWidth)}  {entry.Type.PadRight(typeWidth)}  {value}";
            if (head.Length <= Width)
            {
                page.Line(head);
            }
            else
            {
                page.Line($"  {entry.Path.PadRight(pathWidth)}  {entry.Type}");
                page.Wrapped(value, indent: 6);
            }

            if (!withMeaning)
                continue;
            if (entry.Values.Count > 0)
                page.Wrapped("one of: " + string.Join(", ", entry.Values), indent: 6);
            page.Wrapped(entry.Description, indent: 6);
        }
    }

    private static string Total(SettingsCatalog catalog, string? host)
    {
        var categories = SettingsCategories.All.Count(category => SectionsOf(catalog, category).Count > 0);
        return $"Settings {host ?? "Orkeon"} reads: {Count(catalog.Settings.Count, "key")} in " +
               $"{Count(catalog.Sections.Count, "section")}, under {Count(categories, "category", "categories")}.";
    }

    private static string CategoryHead(SettingsCatalog catalog, SettingsCategory category, IReadOnlyList<SettingsCatalogSection> sections) =>
        $"{category.Title} ({category.Id}) — {Count(sections.Count, "section")}, " +
        Count(sections.Sum(section => catalog.SettingsOf(section.Path).Count()), "key");

    private static List<SettingsCatalogSection> SectionsOf(SettingsCatalog catalog, SettingsCategory category) =>
        [.. catalog.Sections.Where(section => string.Equals(section.Category, category.Id, StringComparison.Ordinal))];

    /// <summary>Who reads a section: the shipped binaries, or the registration a host written in C# calls.</summary>
    private static string ReadBy(SettingsCatalogSection section) =>
        section.Hosts.Count > 0 ? string.Join(", ", section.Hosts)
        : section.Registration is null ? "no shipped binary"
        : $"a C# host only — {section.Registration}()";

    /// <summary>What a key is worth when nothing sets it; a secret says only that it is one.</summary>
    private static string DefaultOf(SettingsCatalogEntry entry)
    {
        if (entry.Secret)
            return "secret";
        if (entry.Default is null)
            return entry.DefaultNote is null ? "no default" : "default: " + entry.DefaultNote;

        var literal = OnOneLine(entry.Default);
        return entry.DefaultNote is null ? "default: " + literal : $"default: {literal} ({entry.DefaultNote})";
    }

    /// <summary>A JSON literal on one line, a space after each comma: a line can be broken there.</summary>
    private static string OnOneLine(string json)
    {
        using var document = JsonDocument.Parse(json);
        return OnOneLine(document.RootElement);
    }

    private static string OnOneLine(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(OnOneLine)) + "]",
        JsonValueKind.Object => "{" + string.Join(", ", value.EnumerateObject().Select(member => $"\"{member.Name}\": {OnOneLine(member.Value)}")) + "}",
        _ => value.GetRawText(),
    };

    /// <summary>A description up to the end of its first sentence.</summary>
    private static string FirstSentence(string text)
    {
        for (var at = text.IndexOf(". ", StringComparison.Ordinal); at >= 0; at = text.IndexOf(". ", at + 1, StringComparison.Ordinal))
        {
            if (at + 2 < text.Length && char.IsUpper(text[at + 2]))
                return text[..(at + 1)];
        }

        return text;
    }

    private static string Count(int count, string one, string? many = null) =>
        count == 1 ? $"1 {one}" : $"{count} {many ?? one + "s"}";

    /// <summary>The text being written, no line wider than <see cref="Width"/> once wrapped.</summary>
    private sealed class Page
    {
        private readonly StringBuilder _text = new();

        public void Line(string line = "") => _text.AppendLine(line);

        /// <summary>Writes <paramref name="text"/> under <paramref name="indent"/> spaces, broken between words — inside one only when it is wider than a line.</summary>
        public void Wrapped(string text, int indent)
        {
            var room = Width - indent;
            var pad = new string(' ', indent);
            var line = new StringBuilder();
            foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var rest = word;
                while (rest.Length > 0)
                {
                    if (line.Length > 0 && line.Length + 1 + rest.Length <= room)
                    {
                        line.Append(' ').Append(rest);
                        rest = string.Empty;
                    }
                    else if (line.Length > 0)
                    {
                        _text.Append(pad).Append(line).AppendLine();
                        line.Clear();
                    }
                    else if (rest.Length <= room)
                    {
                        line.Append(rest);
                        rest = string.Empty;
                    }
                    else
                    {
                        _text.Append(pad).AppendLine(rest[..room]);
                        rest = rest[room..];
                    }
                }
            }

            if (line.Length > 0)
                _text.Append(pad).Append(line).AppendLine();
        }

        public override string ToString() => _text.ToString();
    }
}
