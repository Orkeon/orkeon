using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Raw JSON" screen. XAML wiring only; the document lives in the ViewModel.</summary>
public partial class RawJsonView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public RawJsonView() => InitializeComponent();

    /// <summary>Copies the raw document to the clipboard — presentation-only, no ViewModel involvement.</summary>
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfigTabViewModel { RawJson: { Length: > 0 } json })
        {
            try
            {
                // SetDataObject(copy: false) skips the flush that makes SetText throw when another
                // process (RDP, clipboard managers, VM tools) is holding the Win32 clipboard open.
                Clipboard.SetDataObject(json, copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed locked through the retries — losing one copy beats crashing.
            }
        }
    }
}
