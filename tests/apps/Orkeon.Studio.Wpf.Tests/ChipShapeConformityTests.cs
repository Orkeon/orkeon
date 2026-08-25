using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the pill-shape contract: in WPF — unlike CSS — CornerRadius is NOT clamped to
/// height/2, so a "999" reflex deforms a chip's ends into ogives instead of half-circles.
/// Every pill therefore carries a fixed Height with CornerRadius = Height / 2, exactly,
/// and no XAML may fall back to an oversized radius. The XAML is scanned as text/XML from
/// the source tree; no WPF involved, the suite stays runnable on the Linux runner.
/// </summary>
public sealed partial class ChipShapeConformityTests
{
    [GeneratedRegex("CornerRadius=\"([^\"]+)\"")]
    private static partial Regex CornerRadiusPattern();

    [GeneratedRegex("StaticResource (\\w+)")]
    private static partial Regex StaticResourcePattern();

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(WpfSourceRoot(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Should_Never_Use_An_Oversized_CornerRadius_When_Scanning_All_Xaml()
    {
        // The largest legitimate radius in the design is 28 (the bottom pill bar). Anything
        // above is a CSS "border-radius: 999px" reflex that WPF renders as deformed corners.
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (Match match in CornerRadiusPattern().Matches(File.ReadAllText(file)))
            {
                var components = match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
                if (components.Any(c => double.TryParse(c, CultureInfo.InvariantCulture, out var r) && r > 28))
                {
                    offenders.Add($"{Path.GetFileName(file)}: CornerRadius=\"{match.Groups[1].Value}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0, $"Oversized CornerRadius (WPF does not clamp to height/2): {string.Join("; ", offenders)}");
    }

    [Theory]
    [InlineData("ToolChip", 22.0)]
    [InlineData("MetricChip", 20.0)]
    [InlineData("BadgeBase", 20.0)]
    public void Should_Keep_Radius_At_Exactly_Half_The_Height_When_Reading_The_Pill_Styles(string styleKey, double expectedHeight)
    {
        var document = XDocument.Load(Path.Combine(WpfSourceRoot(), "Themes", "Studio.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        // Walk the BasedOn chain so a derived style (MetricChip) resolves inherited setters.
        var stylesByKey = document.Root!.Elements()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute(x + "Key") is not null)
            .ToDictionary(e => e.Attribute(x + "Key")!.Value);

        string? Setter(string key, string property)
        {
            for (var style = stylesByKey.GetValueOrDefault(key); style is not null;)
            {
                var value = style.Elements()
                    .Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == property)
                    .Select(e => (string?)e.Attribute("Value"))
                    .FirstOrDefault(v => v is not null);
                if (value is not null)
                {
                    return value;
                }

                var basedOn = (string?)style.Attribute("BasedOn");
                var parentKey = basedOn is null ? null : StaticResourcePattern().Match(basedOn).Groups[1].Value;
                style = parentKey is null ? null : stylesByKey.GetValueOrDefault(parentKey);
            }

            return null;
        }

        var height = double.Parse(Setter(styleKey, "Height")!, CultureInfo.InvariantCulture);
        var radius = double.Parse(Setter(styleKey, "CornerRadius")!, CultureInfo.InvariantCulture);

        Assert.Equal(expectedHeight, height);
        Assert.Equal(height / 2, radius);
    }
}
