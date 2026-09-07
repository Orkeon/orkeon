using System.Xml.Linq;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the STUDIO-11 contract between the Core key registry and the WPF resx pair:
/// every key declared in <see cref="StudioStringKeys"/> must exist in Strings.resx with
/// exactly the English text of <see cref="EnglishStudioStrings"/> — otherwise the EN WPF
/// UI would silently drift from the EN default the TUIs and the tests use — and the resx
/// must not carry Core_/Vm_ keys the registry no longer declares.
/// </summary>
public sealed class StudioStringsResxDriftTests
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
    public void Should_HaveNoOrphanCoreOrVmKey_InTheResx()
    {
        var orphans = ReadEnglishResx().Keys
            .Where(k => k.StartsWith("Core_", StringComparison.Ordinal)
                || k.StartsWith("Vm_", StringComparison.Ordinal))
            .Where(k => !EnglishStudioStrings.All.ContainsKey(k))
            .Order()
            .ToList();

        Assert.True(orphans.Count == 0, $"Keys in Strings.resx absent from StudioStringKeys: {string.Join(", ", orphans)}");
    }
}
