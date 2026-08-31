using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The progress card of steps 2 and 3. XAML wiring only — everything it shows
/// comes from <c>ComposeProgressViewModel</c>.</summary>
public partial class ComposeProgressView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public ComposeProgressView() => InitializeComponent();
}
