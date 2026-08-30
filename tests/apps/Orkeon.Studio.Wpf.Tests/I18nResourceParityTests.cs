using System.Xml.Linq;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the localization contract across the five cultures: every visible string goes through
/// I18n, whose lookup falls back to the raw key on a miss — so a key present in one resx and
/// absent from another would silently ship the identifier as a label. The resx are diffed here
/// as plain XML; no WPF involved, the suite stays runnable on the Linux runner.
/// <para>
/// A culture absent from <see cref="Satellites"/> — and from the test csproj's None items — is
/// a culture this suite silently stops checking, which is the one failure mode it exists to
/// prevent. Adding a language means adding it in both places.
/// </para>
/// </summary>
public sealed partial class I18nResourceParityTests
{
    /// <summary>The neutral culture: the key registry of record.</summary>
    private const string Neutral = "Strings.resx";

    /// <summary>Every satellite, by file name.</summary>
    public static readonly string[] Satellites =
        ["Strings.fr.resx", "Strings.es.resx", "Strings.de.resx", "Strings.zh-Hans.resx"];

    /// <summary>All five, for the checks that apply to every file alike.</summary>
    public static TheoryData<string> AllCultures()
    {
        var data = new TheoryData<string> { Neutral };
        foreach (var satellite in Satellites)
            data.Add(satellite);

        return data;
    }

    private static string ResourcePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", fileName);

    private static Dictionary<string, string> ReadEntries(string fileName)
    {
        var document = XDocument.Load(ResourcePath(fileName));

        // GroupBy keeps this reader usable even with a duplicated key, so the dedicated
        // duplicate test is the only one that fails in that case.
        return document.Root!
            .Elements("data")
            .GroupBy(d => (string?)d.Attribute("name") ?? throw new InvalidOperationException("data without name"))
            .ToDictionary(g => g.Key, g => (string?)g.First().Element("value") ?? "");
    }

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void Should_Have_Same_Keys_When_Comparing_A_Satellite_To_The_Neutral_Culture(string satellite)
    {
        var neutral = ReadEntries(Neutral);
        var translated = ReadEntries(satellite);

        var missingInSatellite = neutral.Keys.Except(translated.Keys).Order().ToList();
        var missingInNeutral = translated.Keys.Except(neutral.Keys).Order().ToList();

        Assert.True(missingInSatellite.Count == 0, $"Keys missing in {satellite}: {string.Join(", ", missingInSatellite)}");
        Assert.True(missingInNeutral.Count == 0, $"Keys missing in {Neutral}: {string.Join(", ", missingInNeutral)}");
    }

    /// <summary>The four satellites, one test case each.</summary>
    public static TheoryData<string> SatelliteCultures()
    {
        var data = new TheoryData<string>();
        foreach (var satellite in Satellites)
            data.Add(satellite);

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCultures))]
    public void Should_Have_No_Empty_Values_When_Reading_A_Culture(string fileName)
    {
        var empty = ReadEntries(fileName)
            .Where(e => string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Key)
            .Order()
            .ToList();

        Assert.True(empty.Count == 0, $"Empty values in {fileName}: {string.Join(", ", empty)}");
    }

    [Theory]
    [MemberData(nameof(AllCultures))]
    public void Should_Have_Unique_Keys_When_Reading_A_Culture(string fileName)
    {
        var duplicates = XDocument.Load(ResourcePath(fileName)).Root!
            .Elements("data")
            .GroupBy(d => (string?)d.Attribute("name"))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate keys in {fileName}: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// The virtual mount roots the runner uses. They are code literals an expert screen
    /// quotes verbatim — the same category as <c>--mount</c> or
    /// <c>Orkeon:FileSystem:Mounts:{index}</c>, which the French strings already carry.
    /// Translating <c>/crew</c> would name a mount that does not exist.
    /// </summary>
    private static readonly string[] MountPathLiterals = ["/crew"];

    [Fact]
    public void Should_Speak_Of_Equipes_Not_Crews_When_Reading_The_French_Values()
    {
        // T-07: the mock's vocabulary is the French word for team; "crew" is the engine's word
        // and stays out of every user-visible French string (key NAMES may keep it — identifiers).
        var offenders = ReadEntries("Strings.fr.resx")
            .Where(e => StripMountLiterals(e.Value).Contains("crew", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Key)
            .Order()
            .ToList();

        Assert.True(offenders.Count == 0, $"French values still saying crew: {string.Join(", ", offenders)}");

        static string StripMountLiterals(string value) =>
            MountPathLiterals.Aggregate(value, (text, literal) => text.Replace(literal, "", StringComparison.Ordinal));
    }

    /// <summary>
    /// How many strings a satellite still carries verbatim from English.
    /// <para>
    /// What is left is the irreducible floor, and it is worth naming: product names nobody
    /// translates (Ollama, Anthropic, Qwen, Z.AI…), pure format patterns with no words in
    /// them (« {0} · {1} », « {0} / {1} »), and real cognates — Spanish «normal», German
    /// «{0} Tokens». Everything with a sentence in it has been written down in all four.
    /// </para>
    /// <para>
    /// It stays a CEILING rather than an equality: a new English string lands here before
    /// anyone translates it, and this is what says so out loud instead of letting a
    /// half-translated app look finished. It may only ever be lowered.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, int> TranslationDebtCeiling = new(StringComparer.Ordinal)
    {
        ["Strings.fr.resx"] = 49,
        ["Strings.es.resx"] = 33,
        ["Strings.de.resx"] = 34,
        ["Strings.zh-Hans.resx"] = 27,
    };

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void Should_Not_Grow_The_Translation_Debt_When_Reading_A_Satellite(string satellite)
    {
        var neutral = ReadEntries(Neutral);
        var translated = ReadEntries(satellite);

        // A value identical to the English one is either a real cognate (« Orkeon », « JSON »)
        // or an untranslated string. Counting both is the honest reading: the number only
        // reaches its floor when someone has looked at every one of them.
        var untranslated = translated
            .Count(e => neutral.TryGetValue(e.Key, out var english)
                     && string.Equals(english, e.Value, StringComparison.Ordinal));

        Assert.True(
            untranslated <= TranslationDebtCeiling[satellite],
            $"{satellite}: {untranslated} strings still carry the English text, ceiling is "
            + $"{TranslationDebtCeiling[satellite]}. Lower the ceiling when you lower the debt; never raise it.");
    }

    /// <summary>
    /// Every {N} placeholder of the neutral culture must appear in each satellite, and no
    /// satellite may invent one. This is not cosmetic: a translated pattern that dropped a
    /// placeholder silently loses the value, and one that gained a {2} throws
    /// FormatException the first time that screen renders — in one language only, on a
    /// machine that is not the developer's.
    /// </summary>
    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void Should_Keep_Every_Placeholder_When_Comparing_A_Satellite_To_The_Neutral_Culture(string satellite)
    {
        var neutral = ReadEntries(Neutral);
        var translated = ReadEntries(satellite);
        var offenders = new List<string>();

        foreach (var (key, english) in neutral)
        {
            if (!translated.TryGetValue(key, out var value))
                continue;

            var expected = Placeholders(english);
            var actual = Placeholders(value);
            if (!expected.SetEquals(actual))
            {
                offenders.Add(
                    $"{key}: expected {{{string.Join(",", expected.Order())}}}, found {{{string.Join(",", actual.Order())}}}");
            }
        }

        Assert.True(offenders.Count == 0, $"Placeholder drift in {satellite}: {string.Join("; ", offenders)}");

        static HashSet<string> Placeholders(string text) =>
            [.. PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value)];
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\{(\d+)\}")]
    private static partial System.Text.RegularExpressions.Regex PlaceholderPattern();

    /// <summary>
    /// One convention, in English: <c>Studio.&lt;Screen&gt;.&lt;Label&gt;</c>. The catalogue
    /// used to speak three dialects at once — Nav_Start, Wiz_TeamName, Core_Preset_None_Title
    /// — and a key you cannot guess the shape of is a key someone declares twice.
    /// <para>
    /// The screen list is closed on purpose: a new one is a decision, not a typo, and this is
    /// where it gets made rather than discovered later in a resx diff.
    /// </para>
    /// </summary>
    [Fact]
    public void Should_Name_Every_Key_On_The_One_Convention()
    {
        string[] screens =
        [
            "Shell", "Create", "Chat", "Teams", "Import", "Trial",
            "Run", "History", "Settings", "Diagnostics", "Common",
        ];

        var offenders = ReadEntries(Neutral).Keys
            .Where(key => !screens.Any(screen =>
                key.StartsWith($"Studio.{screen}.", StringComparison.Ordinal)))
            .Order()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Keys off the Studio.<Screen>.<Label> convention: {string.Join(", ", offenders)}");
    }
}
