using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The unified "Réglages" screen. XAML wiring only; every behaviour lives in the ViewModels.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public SettingsView() => InitializeComponent();
}
