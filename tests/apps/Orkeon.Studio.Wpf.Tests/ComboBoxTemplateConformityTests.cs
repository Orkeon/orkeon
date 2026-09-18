using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Pins the closed-box contract of the global <c>ComboBox</c> template (STUDIO-16, D-06).
/// <c>DisplayMemberPath</c> reaches the selection box through the item TEMPLATE SELECTOR
/// — WPF turns the path into a <c>DisplayMemberTemplateSelector</c> and sets it as
/// <c>ItemTemplateSelector</c> — so a template that binds <c>SelectionBoxItemTemplate</c>
/// alone shows the drop-down right and the closed box wrong: <c>ToString()</c> of the item,
/// «MountRightsChoice { Rights = ReadWrite, Token = rw, Label = … }» on the rights lists,
/// the record on the team picker. The stock template binds four things on its
/// <c>ContentPresenter</c>; ours must too. The XAML is read as XML from the source tree, no
/// WPF involved, so the suite stays runnable on the Linux runner.
/// </summary>
public sealed class ComboBoxTemplateConformityTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(WpfSourceRoot(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>The <c>ContentPresenter</c> of the implicit <c>ComboBox</c> style's control template.</summary>
    private static XElement SelectionBoxPresenter()
    {
        var document = XDocument.Load(Path.Combine(WpfSourceRoot(), "Themes", "Studio.xaml"));

        var style = document.Root!.Elements(Presentation + "Style")
            .Single(e => (string?)e.Attribute("TargetType") == "ComboBox" && e.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Key") is null);
        var template = style.Descendants(Presentation + "ControlTemplate")
            .Single(e => (string?)e.Attribute("TargetType") == "ComboBox");

        // The presenter of the selection box is the one fed the SelectionBoxItem; the popup's
        // items go through an ItemsPresenter, which is a different element.
        return template.Descendants(Presentation + "ContentPresenter")
            .Single(e => ((string?)e.Attribute("Content") ?? "").Contains("SelectionBoxItem", StringComparison.Ordinal));
    }

    [Fact]
    public void The_combo_box_template_binds_the_selection_template_selector()
    {
        var presenter = SelectionBoxPresenter();

        Assert.Equal("{TemplateBinding SelectionBoxItem}", (string?)presenter.Attribute("Content"));
        Assert.Equal("{TemplateBinding SelectionBoxItemTemplate}", (string?)presenter.Attribute("ContentTemplate"));
        Assert.Equal("{TemplateBinding ItemTemplateSelector}", (string?)presenter.Attribute("ContentTemplateSelector"));
        Assert.Equal("{TemplateBinding SelectionBoxItemStringFormat}", (string?)presenter.Attribute("ContentStringFormat"));
    }

    /// <summary>
    /// The template fix covers a <c>ComboBox</c> and nothing else: a <c>DisplayMemberPath</c>
    /// on another items control would need its own reading. Three today — the two rights
    /// lists of the mount editor and the team picker of the trial screen — and every one of
    /// them on a ComboBox.
    /// </summary>
    [Fact]
    public void Every_display_member_path_sits_on_a_combo_box_the_template_covers()
    {
        var usages = new List<string>();
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            var document = XDocument.Load(file);
            foreach (var element in document.Descendants().Where(e => e.Attribute("DisplayMemberPath") is not null))
            {
                usages.Add($"{Path.GetFileName(file)}: {element.Name.LocalName} DisplayMemberPath=\"{element.Attribute("DisplayMemberPath")!.Value}\"");
                if (element.Name.LocalName != "ComboBox")
                    offenders.Add(usages[^1]);
            }
        }

        Assert.True(offenders.Count == 0, $"DisplayMemberPath outside a ComboBox: {string.Join("; ", offenders)}");
        Assert.NotEmpty(usages);
    }
}
