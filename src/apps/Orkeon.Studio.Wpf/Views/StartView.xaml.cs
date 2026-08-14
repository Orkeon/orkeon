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
}
