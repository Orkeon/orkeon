using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the token layer of the 30/08 design pass, the way <see cref="ChipShapeConformityTests"/>
/// pins pill geometry: the XAML is read as text and XML from the source tree, so the suite stays
/// on the Linux runner and never touches WPF.
/// <para>
/// Three contracts. (1) The two theme dictionaries declare the SAME brush keys — a key present in
/// one and missing from the other resolves to nothing after a hot swap, and WPF reports that as
/// silence, not as an error. (2) No view names a colour directly: a hex literal or a
/// <c>StaticResource …Brush</c> both survive the theme swap unchanged, which is the same bug twice.
/// (3) Every <c>Kind="…"</c> an icon asks for exists in Icons.xaml — <c>LucideIcon</c> renders an
/// empty geometry for an unknown name without complaining.
/// </para>
/// </summary>
public sealed partial class DesignTokenConformityTests
{
    [GeneratedRegex("Kind=\"([a-z0-9-]+)\"")]
    private static partial Regex IconKindPattern();

    [GeneratedRegex("x:Key=\"Lucide\\.([a-z0-9-]+)\"")]
    private static partial Regex IconKeyPattern();

    // #RGB is not valid WPF colour syntax, so 6 and 8 digits are the two real shapes.
    [GeneratedRegex("""(?<![\w])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})(?![0-9A-Fa-f])""")]
    private static partial Regex HexLiteralPattern();

    [GeneratedRegex("""StaticResource\s+\w*Brush\b""")]
    private static partial Regex StaticBrushPattern();

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(WpfSourceRoot(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Theme(string fileName) =>
        Path.Combine(WpfSourceRoot(), "Themes", fileName);

    private static IReadOnlyCollection<string> Keys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Root!
            .Elements()
            .Select(e => (string?)e.Attribute(x + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();
    }

    [Fact]
    public void Should_Declare_The_Same_Keys_When_Comparing_The_Two_Theme_Dictionaries()
    {
        var light = Keys(Theme("Tokens.Light.xaml"));
        var dark = Keys(Theme("Tokens.Dark.xaml"));

        var missingInDark = light.Except(dark).Order().ToList();
        var missingInLight = dark.Except(light).Order().ToList();

        Assert.True(missingInDark.Count == 0, $"Keys missing in Tokens.Dark.xaml: {string.Join(", ", missingInDark)}");
        Assert.True(missingInLight.Count == 0, $"Keys missing in Tokens.Light.xaml: {string.Join(", ", missingInLight)}");
    }

    [Fact]
    public void Should_Never_Name_A_Colour_Directly_When_Scanning_Every_Xaml_Outside_The_Theme_Files()
    {
        // The theme dictionaries are where colours are allowed to be literal — that is their job.
        var offenders = new List<string>();

        foreach (var file in XamlFiles().Where(f => !Path.GetFileName(f).StartsWith("Tokens.", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (Match match in HexLiteralPattern().Matches(text))
            {
                offenders.Add($"{name}: hex literal {match.Value}");
            }

            foreach (Match match in StaticBrushPattern().Matches(text))
            {
                offenders.Add($"{name}: {match.Value} (a brush must be DynamicResource — the theme swaps at runtime)");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    [Fact]
    public void Should_Resolve_Every_Requested_Icon_When_Comparing_Kinds_To_The_Geometry_Dictionary()
    {
        var declared = IconKeyPattern()
            .Matches(File.ReadAllText(Theme("Icons.xaml")))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in XamlFiles())
        {
            foreach (Match match in IconKindPattern().Matches(File.ReadAllText(file)))
            {
                if (!declared.Contains(match.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"Icon Kind with no Lucide geometry: {string.Join(", ", missing)}");
    }
}
