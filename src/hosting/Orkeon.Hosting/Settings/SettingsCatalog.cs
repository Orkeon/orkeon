using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Orkeon.Constants.Configuration;

namespace Orkeon.Hosting;

/// <summary>
/// One section of the settings catalogue: a path a reader binds or reads raw — <c>RateLimiting</c>,
/// <c>Orkeon:CodeSandbox:Docker</c> —, with who reads it.
/// </summary>
internal sealed record SettingsCatalogSection
{
    /// <summary>The section's configuration path.</summary>
    public required string Path { get; init; }

    /// <summary>The category it is filed under: an id of <see cref="SettingsCategories"/>.</summary>
    public required string Category { get; init; }

    /// <summary>What the section configures, in one sentence: the summary of the type it is read into.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The shipped hosts that read it, among <see cref="SettingsHosts.All"/>; empty when none does —
    /// a host written in C# reads it through <see cref="Registration"/>.
    /// </summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];

    /// <summary>
    /// The public registration a host written in C# reads the section through
    /// (<c>AddOrkeonToolRateLimiting</c>), or null when only a shipped host's own composition reads it.
    /// </summary>
    public string? Registration { get; init; }
}

/// <summary>
/// One key of the settings catalogue. A name the operator chooses is written <c>&lt;name&gt;</c>
/// (<c>Llm:Profiles:&lt;name&gt;:Model</c>) and an index <c>&lt;i&gt;</c>
/// (<c>Orkeon:Host:Crews:&lt;i&gt;:Path</c>).
/// </summary>
internal sealed record SettingsCatalogEntry
{
    /// <summary>The key's configuration path.</summary>
    public required string Path { get; init; }

    /// <summary>The path of the section the key belongs to: the deepest declared one above it.</summary>
    public required string Section { get; init; }

    /// <summary>
    /// What the key holds: <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>,
    /// <c>duration</c>, <c>uri</c>, <c>date-time</c>, <c>enum</c> (its names are <see cref="Values"/>),
    /// <c>list of …</c>, or <c>any</c> for a value its reader takes as it is written.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The value the key has when nothing sets it, as a JSON literal (<c>60</c>, <c>"fast"</c>,
    /// <c>[]</c>), or null when there is none — or none that can be written: a secret, a value that
    /// depends on the machine or on the moment (<see cref="DefaultNote"/> then says which).
    /// </summary>
    public string? Default { get; init; }

    /// <summary>
    /// What the literal cannot say of the default, in words: what stands for it when it is no
    /// constant ("the moment the value is read"), the entries a dictionary holds before any is
    /// written ("WebScrapeTool = 10, …"), or what it depends on ("under the default profile,
    /// `fast`: each profile sets its own").
    /// </summary>
    public string? DefaultNote { get; init; }

    /// <summary>The values the key accepts when they are a closed list — an enumeration's names —, else empty.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>Whether the value is a secret: no surface prints one, a default included.</summary>
    public bool Secret { get; init; }

    /// <summary>What the key means: the summary of the property it is read into.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// The settings a host reads, listed: every section, every key with its type, its default, its
/// allowed values and its meaning, filed under the categories of <see cref="SettingsCategories"/>,
/// each section naming the hosts that read it.
/// <para>
/// <see cref="Complete"/> is the whole of it — what the three shipped binaries read and what a host
/// written in C# can read through a public registration —, embedded when this assembly is built: it
/// opens no file, reads no setting of the machine and builds no container, so it answers the same on
/// a machine whose settings file is refused. <see cref="ForHost"/> narrows it to one binary.
/// </para>
/// <para>
/// The embedded files are produced, never written by hand: <see cref="SettingsCatalogBuilder"/>
/// reads the sections each composition declares (<c>SettingsDeclaration</c>) the way
/// <see cref="SettingsShape"/> reads their keys, and a test of each host's project holds its file to
/// what that host really declares.
/// </para>
/// </summary>
internal sealed class SettingsCatalog
{
    private const string SectionsField = "sections";
    private const string SettingsField = "settings";

    private static readonly Lazy<SettingsCatalog> s_complete = new(LoadComplete);

    private readonly Dictionary<string, SettingsCatalogSection> _sections;

    public SettingsCatalog(IEnumerable<SettingsCatalogSection> sections, IEnumerable<SettingsCatalogEntry> settings)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(settings);

        Sections = [.. sections
            .OrderBy(section => CategoryRank(section.Category))
            .ThenBy(section => section.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(section => section.Path, StringComparer.Ordinal)];
        _sections = Sections.ToDictionary(section => section.Path, StringComparer.OrdinalIgnoreCase);

        var rank = Sections.Select((section, index) => (section.Path, index))
            .ToDictionary(pair => pair.Path, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        Settings = [.. settings
            .OrderBy(entry => rank.GetValueOrDefault(entry.Section, int.MaxValue))
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every setting Orkeon reads: the sections of the three shipped binaries and those a public
    /// registration reads in a host written in C#, each saying who reads it.
    /// </summary>
    public static SettingsCatalog Complete => s_complete.Value;

    /// <summary>The sections, by category in the order of <see cref="SettingsCategories.All"/>, then by path.</summary>
    public IReadOnlyList<SettingsCatalogSection> Sections { get; }

    /// <summary>The keys, in the order of their sections, then by path.</summary>
    public IReadOnlyList<SettingsCatalogEntry> Settings { get; }

    /// <summary>The section at <paramref name="path"/>, any case, or null.</summary>
    public SettingsCatalogSection? Section(string path) => _sections.GetValueOrDefault(path);

    /// <summary>The keys of the section at <paramref name="sectionPath"/>, in the catalogue's order.</summary>
    public IEnumerable<SettingsCatalogEntry> SettingsOf(string sectionPath) =>
        Settings.Where(entry => string.Equals(entry.Section, sectionPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>The key at <paramref name="path"/>, any case, or null.</summary>
    public SettingsCatalogEntry? Setting(string path) =>
        Settings.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>What one shipped host reads: the sections naming <paramref name="host"/>, and their keys.</summary>
    /// <param name="host">A host of <see cref="SettingsHosts.All"/>.</param>
    public SettingsCatalog ForHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return Where(section => section.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>What at least one shipped host reads: everything but the sections a C# host alone can read.</summary>
    public SettingsCatalog ForShippedHosts() => Where(section => section.Hosts.Count > 0);

    /// <summary>The sections <paramref name="keep"/> accepts, and their keys.</summary>
    public SettingsCatalog Where(Func<SettingsCatalogSection, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        var kept = Sections.Where(keep).ToList();
        var paths = kept.Select(section => section.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SettingsCatalog(kept, Settings.Where(entry => paths.Contains(entry.Section)));
    }

    /// <summary>
    /// The deepest section of the catalogue at or above <paramref name="path"/> — the one a key
    /// written there belongs to —, or null when no section of the catalogue covers it.
    /// </summary>
    public SettingsCatalogSection? SectionCovering(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        for (var candidate = path; candidate.Length > 0;)
        {
            if (_sections.TryGetValue(candidate, out var section))
                return section;

            var cut = candidate.LastIndexOf(ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
            candidate = cut < 0 ? string.Empty : candidate[..cut];
        }

        return null;
    }

    /// <summary>
    /// The catalogue as JSON, stable: the same catalogue gives the same bytes on every machine —
    /// sections and keys in the catalogue's order, no date, no path of the machine, line feeds.
    /// A field without a value is left out (<c>default</c>, <c>defaultNote</c>, <c>registration</c>);
    /// <c>values</c> and <c>secret</c> appear only when they say something.
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            Write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>Reads what <see cref="ToJson"/> wrote.</summary>
    /// <exception cref="JsonException">The text is not a catalogue.</exception>
    public static SettingsCatalog FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    internal static JsonWriterOptions WriterOptions { get; } = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        // The file is read by people too: `<name>`, a backtick and an em dash stay what they are.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes the catalogue's two fields into the object <paramref name="writer"/> is in.</summary>
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartArray(SectionsField);
        foreach (var section in Sections)
            WriteSection(writer, section);
        writer.WriteEndArray();

        writer.WriteStartArray(SettingsField);
        foreach (var entry in Settings)
            WriteEntry(writer, entry);
        writer.WriteEndArray();
    }

    private static void WriteSection(Utf8JsonWriter writer, SettingsCatalogSection section)
    {
        writer.WriteStartObject();
        writer.WriteString("path", section.Path);
        writer.WriteString("category", section.Category);
        WriteStrings(writer, "hosts", section.Hosts);
        if (section.Registration is not null)
            writer.WriteString("registration", section.Registration);
        writer.WriteString("description", section.Description);
        writer.WriteEndObject();
    }

    private static void WriteEntry(Utf8JsonWriter writer, SettingsCatalogEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("path", entry.Path);
        writer.WriteString("section", entry.Section);
        writer.WriteString("type", entry.Type);
        if (entry.Default is not null)
        {
            writer.WritePropertyName("default");
            writer.WriteRawValue(entry.Default, skipInputValidation: false);
        }

        if (entry.DefaultNote is not null)
            writer.WriteString("defaultNote", entry.DefaultNote);
        if (entry.Values.Count > 0)
            WriteStrings(writer, "values", entry.Values);
        if (entry.Secret)
            writer.WriteBoolean("secret", true);
        writer.WriteString("description", entry.Description);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    /// <summary>Reads the catalogue's two fields from <paramref name="root"/>; a field that is absent is empty.</summary>
    internal static SettingsCatalog Read(JsonElement root)
    {
        var sections = new List<SettingsCatalogSection>();
        if (root.TryGetProperty(SectionsField, out var sectionList))
        {
            foreach (var section in sectionList.EnumerateArray())
            {
                sections.Add(new SettingsCatalogSection
                {
                    Path = Text(section, "path") ?? throw new JsonException("A section of the settings catalogue has no path."),
                    Category = Text(section, "category") ?? string.Empty,
                    Hosts = Texts(section, "hosts"),
                    Registration = Text(section, "registration"),
                    Description = Text(section, "description") ?? string.Empty,
                });
            }
        }

        var settings = new List<SettingsCatalogEntry>();
        if (root.TryGetProperty(SettingsField, out var settingList))
        {
            foreach (var entry in settingList.EnumerateArray())
            {
                settings.Add(new SettingsCatalogEntry
                {
                    Path = Text(entry, "path") ?? throw new JsonException("A key of the settings catalogue has no path."),
                    Section = Text(entry, "section") ?? string.Empty,
                    Type = Text(entry, "type") ?? string.Empty,
                    Default = entry.TryGetProperty("default", out var value) ? value.GetRawText() : null,
                    DefaultNote = Text(entry, "defaultNote"),
                    Values = Texts(entry, "values"),
                    Secret = entry.TryGetProperty("secret", out var secret) && secret.ValueKind == JsonValueKind.True,
                    Description = Text(entry, "description") ?? string.Empty,
                });
            }
        }

        return new SettingsCatalog(sections, settings);
    }

    private static string? Text(JsonElement element, string field) =>
        element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string[] Texts(JsonElement element, string field) =>
        element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(item => item.GetString() ?? string.Empty)]
            : [];

    private static int CategoryRank(string category)
    {
        for (var index = 0; index < SettingsCategories.All.Count; index++)
        {
            if (string.Equals(SettingsCategories.All[index].Id, category, StringComparison.Ordinal))
                return index;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// The embedded files, put together: the library's catalogue, then each shipped host's — the
    /// sections it reads, and the catalogue of those only its binary knows.
    /// </summary>
    private static SettingsCatalog LoadComplete()
    {
        var library = FromJson(Resource(SettingsCatalogFiles.Library));
        var hosts = SettingsHosts.All.Select(host => SettingsHostCatalog.FromJson(Resource(SettingsCatalogFiles.Of(host)))).ToList();
        return Assemble(library, hosts);
    }

    /// <summary>
    /// One catalogue from the library's and the shipped hosts': every section, its
    /// <see cref="SettingsCatalogSection.Hosts"/> the hosts that read it, in the order of
    /// <see cref="SettingsHosts.All"/>.
    /// </summary>
    internal static SettingsCatalog Assemble(SettingsCatalog library, IReadOnlyList<SettingsHostCatalog> hosts)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(hosts);

        var sections = library.Sections.ToDictionary(section => section.Path, StringComparer.OrdinalIgnoreCase);
        var settings = library.Settings.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var own in hosts.Select(host => host.Own))
        {
            foreach (var section in own.Sections)
                sections.TryAdd(section.Path, section);
            foreach (var entry in own.Settings)
                settings.TryAdd(entry.Path, entry);
        }

        var readers = SettingsHosts.All.ToDictionary(
            host => host,
            host => hosts.Where(candidate => string.Equals(candidate.Host, host, StringComparison.Ordinal))
                .SelectMany(candidate => candidate.Reads)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);
        return new SettingsCatalog(
            sections.Values.Select(section => section with
            {
                Hosts = [.. SettingsHosts.All.Where(host => readers[host].Contains(section.Path))],
            }),
            settings.Values);
    }

    private static string Resource(string name)
    {
        var assembly = typeof(SettingsCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The settings catalogue is not embedded: {name} is missing from {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// What one shipped host reads, as its test project wrote it: the paths of every section its
/// composition reads, and the catalogue of the sections only its binary knows — those whose type the
/// library cannot see (<c>Orkeon:Host</c> lives in <c>orkeon-host</c>).
/// </summary>
internal sealed class SettingsHostCatalog
{
    public SettingsHostCatalog(string host, IEnumerable<string> reads, SettingsCatalog own)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(own);

        Host = host;
        Reads = [.. reads.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)];
        Own = own;
    }

    /// <summary>The host: one of <see cref="SettingsHosts.All"/>.</summary>
    public string Host { get; }

    /// <summary>The path of every section the host reads, sorted.</summary>
    public IReadOnlyList<string> Reads { get; }

    /// <summary>The sections and keys no other composition describes.</summary>
    public SettingsCatalog Own { get; }

    /// <summary>
    /// What <paramref name="host"/> reads, given the catalogue its composition describes
    /// (<paramref name="described"/>) and the library's: every section of the first is read, and those
    /// the second does not know are the host's own.
    /// </summary>
    public static SettingsHostCatalog Of(string host, SettingsCatalog described, SettingsCatalog library)
    {
        ArgumentNullException.ThrowIfNull(described);
        ArgumentNullException.ThrowIfNull(library);
        return new SettingsHostCatalog(
            host,
            described.Sections.Select(section => section.Path),
            described.Where(section => library.Section(section.Path) is null));
    }

    /// <summary>
    /// The keys <paramref name="described"/> holds under a section of <paramref name="library"/> that
    /// the library does not list: a host that reads more of a section than the registration it calls
    /// declares. Empty, or the catalogue would miss them — the host's file carries its own sections only.
    /// </summary>
    public static IReadOnlyList<string> KeysTheLibraryLacks(SettingsCatalog described, SettingsCatalog library)
    {
        ArgumentNullException.ThrowIfNull(described);
        ArgumentNullException.ThrowIfNull(library);
        return
        [
            .. described.Settings
                .Where(entry => library.Section(entry.Section) is not null && library.Setting(entry.Path) is null)
                .Select(entry => entry.Path),
        ];
    }

    /// <summary>The host's file, as stable as <see cref="SettingsCatalog.ToJson"/>.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, SettingsCatalog.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("host", Host);
            writer.WriteStartArray("reads");
            foreach (var path in Reads)
                writer.WriteStringValue(path);
            writer.WriteEndArray();
            Own.Write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>Reads what <see cref="ToJson"/> wrote.</summary>
    public static SettingsHostCatalog FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new SettingsHostCatalog(
            root.GetProperty("host").GetString() ?? throw new JsonException("A host's settings catalogue names no host."),
            root.GetProperty("reads").EnumerateArray().Select(path => path.GetString() ?? string.Empty),
            SettingsCatalog.Read(root));
    }
}

/// <summary>The shipped binaries that read settings, by the name of their executable.</summary>
internal static class SettingsHosts
{
    /// <summary>The CLI: <c>orkeon run</c> and every verb that builds the runner host.</summary>
    public const string Cli = "orkeon";

    /// <summary>The service host daemon.</summary>
    public const string ServiceHost = "orkeon-host";

    /// <summary>The interactive console.</summary>
    public const string Repl = "orkeon-repl";

    /// <summary>The three, in the order every list of hosts is written in.</summary>
    public static IReadOnlyList<string> All { get; } = [Cli, ServiceHost, Repl];
}

/// <summary>The embedded files of the catalogue, by their resource name — their name under <c>Settings/Catalog</c>.</summary>
internal static class SettingsCatalogFiles
{
    /// <summary>The folder of the files, from the repository's root.</summary>
    public const string RepositoryDirectory = "src/hosting/Orkeon.Hosting/Settings/Catalog";

    /// <summary>What a public registration reads, and the runner host's own composition.</summary>
    public const string Library = "library.json";

    /// <summary>
    /// How the four files are written again, in the order they depend on each other: the library's
    /// first, which the three hosts' tests read from the repository.
    /// </summary>
    public const string Regenerate =
        "UPDATE_PRODUCED_FILES=1 dotnet test --filter \"FullyQualifiedName~SettingsCatalogFile\" on " +
        "tests/hosting/Orkeon.Hosting.Tests, then on tests/scripting/Orkeon.Scripting.Cli.Tests, " +
        "tests/hosting/Orkeon.Host.Tests and tests/apps/Orkeon.ConsoleApp.Tests; then build again, so the new files are embedded.";

    /// <summary>The file of <paramref name="host"/>: <c>orkeon-host.json</c>.</summary>
    public static string Of(string host) => $"{host}.json";
}
