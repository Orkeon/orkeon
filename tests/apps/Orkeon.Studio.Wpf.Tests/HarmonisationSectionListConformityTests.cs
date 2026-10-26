using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the section list of the rc.5 harmonisation (R13, C14, C15, D05) the way
/// <see cref="DesignTokenConformityTests"/> pins the tokens: the theme is read as text from the
/// source tree, so the suite runs on the Linux runner and never loads WPF.
/// </summary>
public sealed partial class HarmonisationSectionListConformityTests
{
    [GeneratedRegex("""<ColumnDefinition\s+Width="(\d+)"\s*/>""")]
    private static partial Regex ColumnWidthPattern();

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static string Theme(string name) => File.ReadAllText(Path.Combine(WpfSourceRoot(), "Themes", name));

    /// <summary>The StepTabs region of Components.xaml: from its banner to the end of the StepTab style.</summary>
    private static string SectionListRegion()
    {
        var text = Theme("Components.xaml");
        var start = text.IndexOf("══ StepTabs (R14", StringComparison.Ordinal);
        Assert.True(start >= 0, "The StepTabs banner is missing from Components.xaml.");

        var tabStyle = text.IndexOf("TargetType=\"{x:Type controls:StepTab}\"", start, StringComparison.Ordinal);
        Assert.True(tabStyle >= 0, "The StepTab style is missing from Components.xaml.");

        var end = text.IndexOf("</Style>", tabStyle, StringComparison.Ordinal);
        return text[start..end];
    }

    private static string TabStyle()
    {
        var region = SectionListRegion();
        return region[region.IndexOf("TargetType=\"{x:Type controls:StepTab}\"", StringComparison.Ordinal)..];
    }

    [Fact]
    public void Should_Size_The_Tab_Column_At_220_When_Reading_The_StepTabs_Template()
    {
        var widths = ColumnWidthPattern().Matches(SectionListRegion()).Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(["220"], widths);
    }

    [Fact]
    public void Should_Draw_No_Ring_Mark_When_Reading_The_StepTab_Template()
    {
        // A stroked ellipse reads as a radio button (C14); the only ellipse left is the filled sand dot.
        var style = TabStyle();

        Assert.DoesNotContain("Stroke=", style, StringComparison.Ordinal);
        Assert.Contains("Kind=\"circle-dashed\"", style, StringComparison.Ordinal);
        Assert.Contains("Kind=\"circle-check\"", style, StringComparison.Ordinal);
        Assert.Contains("<Ellipse x:Name=\"ExpectedMark\" Width=\"8\" Height=\"8\"", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Keep_The_Selected_Tab_In_Place_When_Reading_The_StepTab_Template()
    {
        // C15: no slide on hover, no negative margin that widens the selected tab past the pane.
        var style = TabStyle();

        Assert.DoesNotContain("TranslateTransform", style, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"0,0,-", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Leave_Group_Titles_As_Written_When_Building_The_Group_Bar()
    {
        var code = File.ReadAllText(Path.Combine(WpfSourceRoot(), "Controls", "StepTabs.cs"));

        Assert.DoesNotContain("ToUpper", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Typography.Capitals", SectionListRegion(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ban")]
    [InlineData("circle-dashed")]
    public void Should_Declare_The_Lucide_Geometry_When_Reading_The_Icon_Dictionary(string kind)
    {
        Assert.Contains($"x:Key=\"Lucide.{kind}\"", Theme("Icons.xaml"), StringComparison.Ordinal);
    }
}
