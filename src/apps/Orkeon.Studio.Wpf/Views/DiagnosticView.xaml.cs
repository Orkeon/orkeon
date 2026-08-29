using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Diagnostic" screen. XAML wiring only; every behaviour lives in the ViewModel.</summary>
public partial class DiagnosticView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public DiagnosticView() => InitializeComponent();

    /// <summary>Copies the last report to the clipboard — presentation-only, no ViewModel involvement.</summary>
    private void OnFixInSettings(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
            window.NavSettings.IsChecked = true;
    }

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticViewModel { CanCopyReport: true } diagnostic)
        {
            try
            {
                // SetDataObject(copy: false) skips the flush that makes SetText throw when another
                // process (RDP, clipboard managers, VM tools) is holding the Win32 clipboard open.
                Clipboard.SetDataObject(diagnostic.BuildReport(), copy: false);

                // The copy confirmation shows for ~1.6 s (mock §D-3), then the label comes back.
                diagnostic.ReportCopied = true;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                timer.Tick += (_, _) => { diagnostic.ReportCopied = false; timer.Stop(); };
                timer.Start();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed locked through the retries — losing one copy beats crashing.
            }
        }
    }
}
