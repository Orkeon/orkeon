using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the STUDIO-11 contract between the Core key registry and the WPF resx pair:
/// every key declared in <see cref="StudioStringKeys"/> must exist in Strings.resx with
/// exactly the English text of <see cref="EnglishStudioStrings"/> — otherwise the EN WPF
/// UI would silently drift from the EN default the TUIs and the tests use — and no key
/// of the resx may be an orphan, one nothing on either side ever asks for.
/// <para>
/// The other direction — a key a view asks for and the catalogue does not carry — is
/// pinned by <c>DesignTokenConformityTests.Should_Resolve_Every_Key_A_View_Asks_For</c>.
/// Together the two close the loop in both directions.
/// </para>
/// </summary>
public sealed partial class StudioStringsResxDriftTests
{
    private static Dictionary<string, string> ReadEnglishResx()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.resx");

        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                d => (string?)d.Attribute("name") ?? throw new InvalidOperationException("data without name"),
                d => (string?)d.Element("value") ?? "");
    }

    [Fact]
    public void Should_CarryEveryRegistryKey_WithTheExactEnglishText()
    {
        var resx = ReadEnglishResx();
        var failures = new List<string>();

        foreach (var (key, english) in EnglishStudioStrings.All)
        {
            if (!resx.TryGetValue(key, out var value))
                failures.Add($"missing in Strings.resx: {key}");
            else if (!string.Equals(value, english, StringComparison.Ordinal))
                failures.Add($"drift on {key}: resx '{value}' != registry '{english}'");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The neutral culture is the English one, screen names included. A value that names a
    /// button in French sends the English reader hunting for a label that screen never
    /// shows — "Dossiers autorises" where the tab reads "Authorized folders" — and neither
    /// parity nor drift sees it, because the key is there and every satellite agrees with
    /// it. An accented letter is the cheap tell, and the one this catalogue can afford:
    /// its English values are otherwise plain ASCII plus typographic punctuation.
    /// </summary>
    [Fact]
    public void Should_SpeakEnglishOnly_InTheNeutralCulture()
    {
        var offenders = ReadEnglishResx()
            .Where(entry => CarriesAnAccentedLetter(entry.Value))
            .Select(entry => $"{entry.Key}: {entry.Value}")
            .Order()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Non-English text in Strings.resx:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// The alphabet the repository's own comment gate polices (scripts/check-comment-accents.py):
    /// Latin-1 Supplement letters plus Latin Extended-A, minus the multiplication and division
    /// signs that sit inside that block. Em dashes, ellipses and quotation marks are punctuation,
    /// not accents, and the English catalogue leans on them everywhere.
    /// </summary>
    private static bool CarriesAnAccentedLetter(string value) =>
        value.Any(c => c is (>= '\u00c0' and <= '\u00ff' and not '\u00d7' and not '\u00f7')
                         or (>= '\u0100' and <= '\u017f'));

    [Fact]
    public void Should_HaveNoOrphanKey_InTheResx()
    {
        var orphans = Orphans(ReadEnglishResx().Keys);

        Assert.True(orphans.Count == 0, $"Keys in Strings.resx nothing consumes: {string.Join(", ", orphans)}");
    }

    /// <summary>
    /// A gate that cannot fail is not a gate. This one used to filter on the Core_/Vm_
    /// prefixes the STUDIO-11 rename retired, so it ran over a catalogue where no key can
    /// match and reported nothing, forever. The ghost below is the proof the rewrite is
    /// worth its runtime: a key the catalogue declares and nothing consumes must come back
    /// named.
    /// </summary>
    [Fact]
    public void Should_NameAGhostKey_WhenNothingConsumesIt()
    {
        const string ghost = "Studio.Common.Ghost";

        var orphans = Orphans([.. ReadEnglishResx().Keys, ghost]);

        Assert.Equal(ghost, Assert.Single(orphans));
    }

    /// <summary>
    /// What consumes a catalogue key, and there are only two things that can: the Core
    /// registry — <see cref="EnglishStudioStrings"/>, which the two TUIs read and the WPF
    /// I18n falls back to — and a literal in the WPF source tree, whether it is written
    /// <c>{Binding [Studio.X.Y]}</c> in XAML, <c>I18n.T("Studio.X.Y")</c> or
    /// <c>_strings["Studio.X.Y"]</c> in C#. A key reached by neither is dead weight that
    /// still costs four translations and goes stale where nobody can see it.
    /// <para>
    /// A key assembled at runtime — <c>CheckKeyPrefix + doctor's own name for the check</c>
    /// — no text sweep can resolve, and this one does not try: those live in the registry,
    /// which is where a key nobody can grep for belongs.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> Orphans(IEnumerable<string> catalogueKeys)
    {
        var consumed = new HashSet<string>(EnglishStudioStrings.All.Keys, StringComparer.Ordinal);

        foreach (var file in WpfSources())
        {
            foreach (Match match in LiteralKeyPattern().Matches(File.ReadAllText(file)))
                consumed.Add(match.Groups[1].Value);
        }

        return [.. catalogueKeys.Where(key => !consumed.Contains(key)).Order()];
    }

    /// <summary>
    /// A key on the reading side is always delimited — quoted in C#, bracketed inside a
    /// binding path — which is what keeps a namespace segment (<c>Orkeon.Studio.Core.Events</c>)
    /// from passing itself off as a reference.
    /// </summary>
    [GeneratedRegex("""["\[](Studio\.[A-Za-z0-9_.\-]+)["\]]""")]
    private static partial Regex LiteralKeyPattern();

    /// <summary>
    /// The WPF sources are read from the source tree rather than the build output, the way
    /// every other conformity suite here does it: the views are XAML, so no compiled
    /// artifact carries the bindings this sweep is looking for.
    /// </summary>
    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static IEnumerable<string> WpfSources() =>
        Directory.EnumerateFiles(WpfSourceRoot(), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin", StringComparison.Ordinal));
}
