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
