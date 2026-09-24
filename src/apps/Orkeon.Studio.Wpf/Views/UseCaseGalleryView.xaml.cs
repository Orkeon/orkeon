using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The use-case gallery's side panel (STUDIO-39). XAML wiring only — everything it shows
/// comes from <c>UseCaseGalleryViewModel</c>.</summary>
public partial class UseCaseGalleryView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public UseCaseGalleryView() => InitializeComponent();
}
