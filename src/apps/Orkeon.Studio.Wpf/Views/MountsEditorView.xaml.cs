using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>
/// The mount form of spec §4.5, reused by both tabs. Code-behind is XAML wiring only.
/// </summary>
public partial class MountsEditorView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public MountsEditorView() => InitializeComponent();
}
