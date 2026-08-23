using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "JSON brut" settings tab: the file, its location and its mirror.</summary>
public partial class SettingsFileView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public SettingsFileView() => InitializeComponent();

    /// <summary>
    /// Write-back for the custom-location PickRow: its IsChecked binding is OneWay (like the global
    /// row, which goes through UseGlobalCommand), so checking it must switch the mode explicitly —
    /// otherwise Save would keep targeting the global file while the row looks selected.
    /// </summary>
    private void OnCustomLocationChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Config.ConfigTabViewModel vm)
        {
            vm.Location.Mode = ViewModels.Config.SettingsLocationMode.CustomPath;
        }
    }
}
