using System.Xml.Linq;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the EN/FR localization contract: every visible string goes through I18n, whose lookup
/// falls back to the raw key on a miss — so a key present in one resx and absent from the other
/// would silently ship an untranslated label. The resx are diffed here as plain XML; no WPF
/// involved, the suite stays runnable on the Linux runner.
/// </summary>
public sealed class I18nResourceParityTests
{
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

    [Fact]
    public void Should_Have_Same_Keys_When_Comparing_French_To_English()
    {
        var english = ReadEntries("Strings.resx");
        var french = ReadEntries("Strings.fr.resx");

        var missingInFrench = english.Keys.Except(french.Keys).Order().ToList();
        var missingInEnglish = french.Keys.Except(english.Keys).Order().ToList();

        Assert.True(missingInFrench.Count == 0, $"Keys missing in Strings.fr.resx: {string.Join(", ", missingInFrench)}");
        Assert.True(missingInEnglish.Count == 0, $"Keys missing in Strings.resx: {string.Join(", ", missingInEnglish)}");
    }

    [Fact]
    public void Should_Have_No_Empty_Values_When_Reading_Both_Resx()
    {
        foreach (var fileName in new[] { "Strings.resx", "Strings.fr.resx" })
        {
            var empty = ReadEntries(fileName)
                .Where(e => string.IsNullOrWhiteSpace(e.Value))
                .Select(e => e.Key)
                .Order()
                .ToList();

            Assert.True(empty.Count == 0, $"Empty values in {fileName}: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Should_Have_Unique_Keys_When_Reading_Both_Resx()
    {
        foreach (var fileName in new[] { "Strings.resx", "Strings.fr.resx" })
        {
            var duplicates = XDocument.Load(ResourcePath(fileName)).Root!
                .Elements("data")
                .GroupBy(d => (string?)d.Attribute("name"))
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicates.Count == 0, $"Duplicate keys in {fileName}: {string.Join(", ", duplicates)}");
        }
    }
}
