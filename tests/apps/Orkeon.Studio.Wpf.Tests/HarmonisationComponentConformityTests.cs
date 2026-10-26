using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the shared components of the rc.5 display harmonisation (lot fb) in
/// Themes/Components.xaml: the four-tone banner (R07, R12, C16, C22), the one fold (R16), the
/// technical block (R18), the empty state (R17), the status dot (R08), the badge (R12), the
/// property row (R10) and the card action bar's «…» (R23). The XAML is read as XML from the
/// source tree, like <see cref="ChipShapeConformityTests"/>: no WPF, the suite runs on Linux.
/// </summary>
public sealed class HarmonisationComponentConformityTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static XDocument Components() =>
        XDocument.Load(Path.Combine(WpfSourceRoot(), "Themes", "Components.xaml"));

    private static XElement ImplicitStyle(string component) =>
        Components().Root!.Elements()
            .Single(e => e.Name.LocalName == "Style"
                && (string?)e.Attribute("TargetType") == "{x:Type controls:" + component + "}");

    private static XElement KeyedStyle(string key) =>
        Components().Root!.Elements().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(X + "Key") == key);

    private static IEnumerable<XElement> Named(XElement root, string localName) =>
        root.Descendants().Where(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> ToneTriggers(XElement style, string tone) =>
        Named(style, "Trigger").Where(t => (string?)t.Attribute("Property") == "Tone" && (string?)t.Attribute("Value") == tone);

    private static string? IconSetter(XElement trigger, string property) =>
        trigger.Elements()
            .Where(s => s.Name.LocalName == "Setter" && (string?)s.Attribute("TargetName") == "icon"
                && (string?)s.Attribute("Property") == property)
            .Select(s => (string?)s.Attribute("Value"))
            .SingleOrDefault();

    [Fact]
    public void Should_Draw_The_Circle_Slashed_When_The_Alert_Is_An_Error()
    {
        var danger = ToneTriggers(ImplicitStyle("Alert"), "Danger").Where(t => IconSetter(t, "Kind") is not null).ToList();

        Assert.Single(danger);
        Assert.Equal("ban", IconSetter(danger[0], "Kind"));
        Assert.Contains("DangerBrush", IconSetter(danger[0], "Foreground"), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Never_Draw_A_Red_Triangle_When_Reading_The_Alert_Tones()
    {
        var alert = ImplicitStyle("Alert");
        var triangles = Named(alert, "Trigger").Where(t => IconSetter(t, "Kind") == "triangle-alert").ToList();

        Assert.Single(triangles);
        Assert.Equal("Warning", (string?)triangles[0].Attribute("Value"));
        Assert.Contains("WarningIconBrush", IconSetter(triangles[0], "Foreground"), StringComparison.Ordinal);
        Assert.DoesNotContain(Named(alert, "LucideIcon"), i => (string?)i.Attribute("Kind") == "triangle-alert");
    }

    [Theory]
    [InlineData("Success", "circle-check")]
    [InlineData("Warning", "triangle-alert")]
    [InlineData("Danger", "ban")]
    public void Should_Give_Each_Tone_Its_Own_Icon_When_Reading_The_Alert(string tone, string kind)
    {
        var alert = ImplicitStyle("Alert");

        Assert.Contains(ToneTriggers(alert, tone), t => IconSetter(t, "Kind") == kind);
        // Info is the default: the template's icon itself.
        Assert.Equal("info", (string?)Named(alert, "LucideIcon").Single(i => (string?)i.Attribute(X + "Name") == "icon").Attribute("Kind"));
    }

    [Fact]
    public void Should_Set_The_Icon_On_The_First_Line_When_Reading_The_Alert_Template()
    {
        // C22: the icon was top-aligned while the text was centred; both start at the top now,
        // the icon 20 px like the first line.
        var alert = ImplicitStyle("Alert");
        var icon = Named(alert, "LucideIcon").Single(i => (string?)i.Attribute(X + "Name") == "icon");
        var text = Named(alert, "StackPanel").Single(p => (string?)p.Attribute(X + "Name") == "text");

        Assert.Equal("20", (string?)icon.Attribute("Size"));
        Assert.Equal("Top", (string?)icon.Attribute("VerticalAlignment"));
        Assert.Equal("Top", (string?)text.Attribute("VerticalAlignment"));
        Assert.All(Named(text, "TextBlock"), t => Assert.Equal("20", (string?)t.Attribute("LineHeight")));
    }

    [Fact]
    public void Should_Fold_The_Engine_Text_Into_A_Mono_Block_When_The_Alert_Has_Details()
    {
        var alert = ImplicitStyle("Alert");
        var bound = alert.Descendants()
            .Where(e => e.Attributes().Any(a => a.Value.Contains("Binding Details", StringComparison.Ordinal)
                && a.Name.LocalName is "Text"))
            .ToList();

        var block = Assert.Single(bound);
        Assert.Equal("TechBlock", block.Name.LocalName);
        Assert.Equal("Disclosure", block.Parent!.Name.LocalName);
        Assert.Contains("Studio.Common.TechnicalDetails", (string?)block.Parent.Attribute("Header"), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Move_The_Actions_Under_The_Text_When_The_Alert_Is_Narrow()
    {
        var narrow = Named(ImplicitStyle("Alert"), "Trigger")
            .Single(t => (string?)t.Attribute("Property") == "IsWide" && (string?)t.Attribute("Value") == "False");

        Assert.Contains(narrow.Elements(), s => (string?)s.Attribute("TargetName") == "actions"
            && (string?)s.Attribute("Property") == "Grid.Row" && (string?)s.Attribute("Value") == "1");
    }

    [Fact]
    public void Should_Fold_With_A_Chevron_And_Never_An_Eye_When_Reading_The_Disclosure()
    {
        var disclosure = ImplicitStyle("Disclosure");
        var kinds = Named(disclosure, "LucideIcon").Select(i => (string?)i.Attribute("Kind"))
            .Concat(Named(disclosure, "Setter").Where(s => (string?)s.Attribute("Property") == "Kind").Select(s => (string?)s.Attribute("Value")))
            .ToList();

        Assert.Contains("chevron-down", kinds);
        Assert.Contains("chevron-up", kinds);
        Assert.DoesNotContain(kinds, k => k?.StartsWith("eye", StringComparison.Ordinal) == true);

        // The content is collapsed while the fold is closed, shown only by IsExpanded.
        var body = Named(disclosure, "ContentPresenter").Single(p => (string?)p.Attribute(X + "Name") == "body");
        Assert.Equal("Collapsed", (string?)body.Attribute("Visibility"));
    }

    [Fact]
    public void Should_Use_No_Eye_Icon_When_Scanning_The_Shared_Components()
    {
        var offenders = Components().Descendants()
            .SelectMany(e => e.Attributes().Where(a => a.Name.LocalName is "Kind" or "Value"))
            .Where(a => a.Value is "eye" or "eye-off")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Should_Keep_The_Engine_Text_Mono_And_Selectable_When_Reading_The_TechBlock()
    {
        var style = ImplicitStyle("TechBlock");
        var box = Assert.Single(Named(style, "TextBox"));

        Assert.Contains(style.Elements(), s => (string?)s.Attribute("Property") == "FontFamily"
            && ((string?)s.Attribute("Value"))?.Contains("MonoFont", StringComparison.Ordinal) == true);
        Assert.Equal("True", (string?)box.Attribute("IsReadOnly"));
        Assert.Equal("Wrap", (string?)box.Attribute("TextWrapping"));
    }

    [Fact]
    public void Should_Draw_A_Forty_Pixel_Sky_Circle_And_A_Compact_One_When_Reading_The_EmptyState()
    {
        var circles = Named(ImplicitStyle("EmptyState"), "Border")
            .Where(b => ((string?)b.Attribute("Background"))?.Contains("SkyBrush", StringComparison.Ordinal) == true)
            .Select(b => ((string?)b.Attribute("Width"), (string?)b.Attribute("Height"), (string?)b.Attribute("CornerRadius")))
            .ToList();

        Assert.Contains(("40", "40", "20"), circles);
        Assert.Contains(("24", "24", "12"), circles);
    }

    [Fact]
    public void Should_Draw_Grey_Lines_Only_When_Reading_The_Skeleton()
    {
        var style = ImplicitStyle("SkeletonLines");

        Assert.Equal(3, Named(style, "Border").Count());
        Assert.Contains(style.Elements(), s => (string?)s.Attribute("Property") == "Background"
            && (string?)s.Attribute("Value") == "{DynamicResource PaperSunkBrush}");
    }

    [Theory]
    [InlineData(null, "NeutralDotBrush")]
    [InlineData("Attention", "WarningIconBrush")]
    [InlineData("Error", "DangerBrush")]
    [InlineData("Ready", "SuccessBrush")]
    public void Should_Colour_The_Eight_Pixel_Dot_By_Tone_When_Reading_The_StatusDot(string? tone, string brush)
    {
        var style = ImplicitStyle("StatusDot");
        var dot = Assert.Single(Named(style, "Ellipse"));
        Assert.Equal("8", (string?)dot.Attribute("Width"));
        Assert.Equal("8", (string?)dot.Attribute("Height"));

        var fill = tone is null
            ? (string?)dot.Attribute("Fill")
            : (string?)Named(style, "Trigger").Single(t => (string?)t.Attribute("Value") == tone)
                .Elements().Single(s => (string?)s.Attribute("Property") == "Fill").Attribute("Value");
        Assert.Equal("{DynamicResource " + brush + "}", fill);
    }

    [Theory]
    [InlineData("Info", "SkyBrush", "AccentInkBrush")]
    [InlineData("Success", "SuccessWeakBrush", "SuccessInkBrush")]
    [InlineData("Warning", "WarningWeakBrush", "WarningInkBrush")]
    [InlineData("Danger", "DangerWeakBrush", "DangerInkBrush")]
    public void Should_Pair_The_Tint_With_The_Dark_Ink_When_Reading_The_Badge(string tone, string background, string foreground)
    {
        var setters = ToneTriggers(ImplicitStyle("Badge"), tone).Single().Elements()
            .ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));

        Assert.Equal("{DynamicResource " + background + "}", setters["Background"]);
        Assert.Equal("{DynamicResource " + foreground + "}", setters["Foreground"]);
    }

    [Fact]
    public void Should_Give_The_Label_160_Pixels_And_A_12_Pixel_Gap_When_Reading_The_PropertyRow()
    {
        var widths = Named(ImplicitStyle("PropertyRow"), "ColumnDefinition").Select(c => (string?)c.Attribute("Width")).ToList();
        var label = Named(ImplicitStyle("PropertyRow"), "TextBlock").First();

        Assert.Equal(["160", "12", "*", "Auto"], widths);
        Assert.Equal("Wrap", (string?)label.Attribute("TextWrapping"));
        Assert.Null(label.Attribute("TextTrimming"));
    }

    [Fact]
    public void Should_Draw_The_Overflow_As_A_Square_Icon_Button_And_The_Group_Rule_In_The_Field_Border()
    {
        Assert.Equal("{StaticResource IconBtn}", (string?)ImplicitStyle("OverflowMenuButton").Attribute("BasedOn"));

        var rule = KeyedStyle("ActionBarRule").Elements()
            .ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));
        Assert.Equal("1", rule["Width"]);
        Assert.Equal("{DynamicResource FieldBorderBrush}", rule["Fill"]);
    }

    [Fact]
    public void Should_Base_A_Local_Style_On_The_Theme_When_It_Targets_A_Harmonisation_Component()
    {
        // As for Alert or Badge: a local Style without BasedOn drops the theme's template, and the
        // control renders with none — invisible on Linux, plain on Windows.
        var components = new[] { "Disclosure", "TechBlock", "EmptyState", "SkeletonLines", "StatusDot", "OverflowMenuButton" };
        var separator = Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(WpfSourceRoot(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}Themes{separator}", StringComparison.Ordinal)
                && !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .SelectMany(file => XDocument.Load(file).Descendants()
                .Where(e => e.Name.LocalName == "Style" && e.Attribute("BasedOn") is null
                    && components.Any(c => ((string?)e.Attribute("TargetType"))?.TrimEnd('}').EndsWith(":" + c, StringComparison.Ordinal) == true))
                .Select(e => $"{Path.GetFileName(file)}: {(string?)e.Attribute("TargetType")}"))
            .ToList();

        Assert.Empty(offenders);
    }
}
