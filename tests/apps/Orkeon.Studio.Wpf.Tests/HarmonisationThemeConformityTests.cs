using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the shared foundation of the rc.5 display harmonisation (lot fa): the palette's tone
/// tokens every banner and badge draws from (R07, R08, R12, R20), the end of the assistant's
/// off-palette blue (R04), the 40 px fields with one 12 px padding for the value and the
/// watermark (R10, C07–C09), the button and type scales (R05, R11). The XAML is read as text
/// and XML from the source tree, no WPF involved, so the suite stays on the Linux runner.
/// </summary>
public sealed partial class HarmonisationThemeConformityTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [GeneratedRegex("StaticResource (\\w+)")]
    private static partial Regex StaticResourcePattern();

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static string Theme(string fileName) => Path.Combine(WpfSourceRoot(), "Themes", fileName);

    private static XElement StudioRoot() => XDocument.Load(Theme("Studio.xaml")).Root!;

    private static Dictionary<string, XElement> KeyedStyles() =>
        StudioRoot().Elements()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is not null)
            .ToDictionary(e => e.Attribute(X + "Key")!.Value);

    private static XElement ImplicitStyle(string targetType) =>
        StudioRoot().Elements()
            .Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute("TargetType") == targetType
                      && e.Attribute(X + "Key") is null);

    private static string? DirectSetter(XElement style, string property) =>
        style.Elements()
            .Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == property)
            .Select(e => (string?)e.Attribute("Value"))
            .FirstOrDefault(v => v is not null);

    /// <summary>A setter's value, walking the BasedOn chain of keyed styles.</summary>
    private static string? Setter(string key, string property)
    {
        var styles = KeyedStyles();
        for (var style = styles.GetValueOrDefault(key); style is not null;)
        {
            var value = DirectSetter(style, property);
            if (value is not null)
                return value;

            var basedOn = (string?)style.Attribute("BasedOn");
            var parent = basedOn is null ? null : StaticResourcePattern().Match(basedOn).Groups[1].Value;
            style = string.IsNullOrEmpty(parent) ? null : styles.GetValueOrDefault(parent);
        }

        return null;
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(WpfSourceRoot(), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin", StringComparison.Ordinal));

    [Fact]
    public void Should_Never_Use_The_Off_Palette_Assistant_Blue()
    {
        var offenders = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("#D6E8F5", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "#D6E8F5 is off the palette (R04): " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("Tokens.Light.xaml")]
    [InlineData("Tokens.Dark.xaml")]
    public void Should_Declare_Every_Tone_Contract_Key_In_Both_Themes(string dictionary)
    {
        string[] contract =
        [
            "InfoBorderBrush", "SuccessBorderBrush", "WarningBorderBrush", "DangerBorderBrush",
            "SuccessInkBrush", "WarningInkBrush", "DangerInkBrush", "WarningIconBrush",
            "AccentInkBrush", "NeutralDotBrush", "FieldBorderBrush",
            "SkyBrush", "InfoWeakBrush", "SuccessWeakBrush", "WarningWeakBrush", "DangerWeakBrush",
        ];
        var declared = XDocument.Load(Theme(dictionary)).Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .ToHashSet(StringComparer.Ordinal);

        var missing = contract.Where(k => !declared.Contains(k)).ToList();

        Assert.True(missing.Count == 0, $"{dictionary} lacks: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("SuccessInkBrush", "#1F6B55")]
    [InlineData("WarningInkBrush", "#7A4F0E")]
    [InlineData("DangerInkBrush", "#9E3227")]
    [InlineData("WarningIconBrush", "#D9A441")]
    [InlineData("AccentInkBrush", "#186670")]
    [InlineData("NeutralDotBrush", "#8A99A5")]
    [InlineData("FieldBorderBrush", "#C9D3D2")]
    [InlineData("SkyBrush", "#E6F1F2")]
    public void Should_Hold_The_Palette_Value_When_Reading_The_Light_Tokens(string key, string colour)
    {
        var brush = XDocument.Load(Theme("Tokens.Light.xaml")).Root!.Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == key);

        Assert.Equal(colour, (string?)brush.Attribute("Color"), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Input")]
    [InlineData("InputPassword")]
    public void Should_Size_A_Field_At_40_With_A_12_Px_Padding(string key)
    {
        Assert.Equal("40", Setter(key, "Height"));
        Assert.StartsWith("12,", Setter(key, "Padding"), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Size_The_Combo_Box_At_40_With_A_12_Px_Padding()
    {
        var combo = ImplicitStyle("ComboBox");

        Assert.Equal("40", DirectSetter(combo, "Height"));
        Assert.StartsWith("12,", DirectSetter(combo, "Padding"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A TextBox applies its Padding inside its content host: a host that also takes the Padding
    /// as its Margin counts it twice, and the typed value starts 12 px right of the watermark
    /// (C08: the value at 23 px, the watermark at 10 px).
    /// </summary>
    [Theory]
    [InlineData("Input")]
    [InlineData("InputPassword")]
    public void Should_Not_Apply_The_Padding_Twice_When_Reading_The_Field_Template(string key)
    {
        var host = KeyedStyles()[key].Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == "PART_ContentHost");

        Assert.DoesNotContain("Padding", (string?)host.Attribute("Margin") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Start_The_Watermark_On_The_Padding_When_Reading_The_Text_Box_Template()
    {
        var watermark = KeyedStyles()["Input"].Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == "watermark");

        Assert.Equal("{TemplateBinding Padding}", (string?)watermark.Attribute("Margin"));
    }

    [Fact]
    public void Should_Align_The_Combo_Value_And_Watermark_And_Reserve_The_Chevron()
    {
        var template = ImplicitStyle("ComboBox").Descendants()
            .Single(e => e.Name.LocalName == "ControlTemplate" && (string?)e.Attribute("TargetType") == "ComboBox");
        var presenter = template.Descendants()
            .Single(e => e.Name.LocalName == "ContentPresenter"
                      && ((string?)e.Attribute("Content") ?? "").Contains("SelectionBoxItem", StringComparison.Ordinal));
        var watermark = template.Descendants()
            .Single(e => (string?)e.Attribute(X + "Name") == "watermark");

        var margin = (string?)presenter.Attribute("Margin");
        Assert.Equal(margin, (string?)watermark.Attribute("Margin"));
        var parts = margin!.Split(',').Select(double.Parse).ToArray();
        Assert.Equal(12, parts[0]);
        Assert.True(parts[2] >= 36, $"The chevron needs 36 px on the right, the presenter leaves {parts[2]}.");
        Assert.Contains(presenter.Descendants(), e => e.Name.LocalName == "Setter"
            && (string?)e.Attribute("Property") == "TextTrimming");
    }

    [Theory]
    [InlineData("ComboS", "160")]
    [InlineData("ComboM", "320")]
    public void Should_Offer_Fixed_Combo_Widths(string key, string width)
    {
        Assert.Equal(width, Setter(key, "Width"));
        Assert.Equal("{StaticResource {x:Type ComboBox}}", (string?)KeyedStyles()[key].Attribute("BasedOn"));
    }

    [Fact]
    public void Should_Stretch_The_Large_Combo()
    {
        Assert.Equal("Stretch", Setter("ComboL", "HorizontalAlignment"));
        Assert.Null(Setter("ComboL", "Width"));
    }

    [Theory]
    [InlineData("BtnPrimary")]
    [InlineData("BtnSecondary")]
    [InlineData("BtnGhost")]
    [InlineData("BtnDanger")]
    [InlineData("BtnDangerSolid")]
    [InlineData("BtnAssistant")]
    public void Should_Draw_Every_Button_Form_At_40_From_BtnBase(string key)
    {
        Assert.Equal("40", Setter(key, "Height"));
    }

    [Fact]
    public void Should_Draw_The_Title_Bar_Buttons_As_32_Px_Ghosts()
    {
        Assert.Equal("{StaticResource BtnGhost}", (string?)KeyedStyles()["BtnTitleBar"].Attribute("BasedOn"));
        Assert.Equal("32", Setter("BtnTitleBar", "controls:ButtonSize.Height"));
    }

    [Theory]
    [InlineData("IconBtn")]
    [InlineData("IconBtnGhost")]
    public void Should_Draw_A_Square_Icon_Button_At_32_With_Radius_6(string key)
    {
        Assert.Equal("32", Setter(key, "Width"));
        Assert.Equal("32", Setter(key, "Height"));
        Assert.Equal("6", Setter(key, "controls:ButtonSize.CornerRadius"));
    }

    [Fact]
    public void Should_Make_The_Assistant_Button_A_Secondary()
    {
        Assert.Equal("{StaticResource BtnSecondary}", (string?)KeyedStyles()["BtnAssistant"].Attribute("BasedOn"));
    }

    [Fact]
    public void Should_Draw_The_Button_Icon_From_The_Attached_Kind()
    {
        var icons = KeyedStyles()["BtnBase"].Descendants()
            .Where(e => e.Name.LocalName == "LucideIcon")
            .ToList();

        Assert.Equal(2, icons.Count);
        Assert.All(icons, i => Assert.Equal("{TemplateBinding controls:ButtonIcon.Kind}", (string?)i.Attribute("Kind")));
        Assert.All(icons, i => Assert.Equal("16", (string?)i.Attribute("Size")));
    }

    [Theory]
    [InlineData("H1", "FontSize", "32")]
    [InlineData("H1", "FontFamily", "{StaticResource DisplayFont}")]
    [InlineData("ModalTitle", "FontSize", "20")]
    [InlineData("ModalTitle", "FontWeight", "SemiBold")]
    [InlineData("CardTitle", "FontSize", "16")]
    [InlineData("CardTitle", "FontWeight", "SemiBold")]
    [InlineData("Subhead", "FontSize", "14")]
    [InlineData("Subhead", "FontWeight", "SemiBold")]
    [InlineData("BodyText", "FontSize", "14")]
    [InlineData("BodyText", "LineHeight", "22")]
    [InlineData("MetaText", "Foreground", "{DynamicResource InkFaintBrush}")]
    [InlineData("Eyebrow", "FontFamily", "{StaticResource MonoFont}")]
    public void Should_Follow_The_Type_Scale(string key, string property, string value)
    {
        Assert.Equal(value, Setter(key, property));
    }

    [Theory]
    [InlineData("BadgeOk", "{DynamicResource SuccessInkBrush}")]
    [InlineData("BadgeWarn", "{DynamicResource WarningInkBrush}")]
    [InlineData("BadgeFail", "{DynamicResource DangerInkBrush}")]
    [InlineData("BadgeSky", "{DynamicResource AccentInkBrush}")]
    [InlineData("BadgeRun", "{DynamicResource AccentInkBrush}")]
    public void Should_Write_A_Badge_In_The_Dark_Ink_Of_Its_Tone(string key, string ink)
    {
        Assert.Equal(ink, Setter(key, "TextElement.Foreground"));
    }

    [Fact]
    public void Should_Bound_The_Page_Container_At_1040()
    {
        Assert.Equal("1040", Setter("PageContainer", "MaxWidth"));
        Assert.Equal("1040", Setter("PageColumn", "MaxWidth"));
    }
}
