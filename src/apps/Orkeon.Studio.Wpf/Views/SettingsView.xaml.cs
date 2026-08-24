using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The unified "Réglages" screen. XAML wiring only; every behaviour lives in the ViewModels.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public SettingsView() => InitializeComponent();

    /// <summary>Copies the last validation to the clipboard — presentation-only.</summary>
    private void OnCopyValidation(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Config.CanCopyValidation: true } shell)
        {
            try
            {
                // Same lock-tolerant pattern as RawJsonView: SetDataObject(copy: false) skips the
                // flush that throws when another process holds the Win32 clipboard open.
                Clipboard.SetDataObject(shell.Config.BuildValidationReport(), copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed locked through the retries — losing one copy beats crashing.
            }
        }
    }
}
