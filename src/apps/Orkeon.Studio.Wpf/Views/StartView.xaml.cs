using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Wpf.Views;

public partial class StartView : UserControl
{
    public StartView() => InitializeComponent();

    /// <summary>Routes a preset PickRow check to Presets.SelectedPreset (the ItemsControl has no selection of its own).</summary>
    private void OnPresetChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: LlmPresetInfo preset }
            && DataContext is ViewModels.Config.ConfigTabViewModel vm)
        {
            vm.Presets.SelectedPreset = preset;
        }
    }

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
