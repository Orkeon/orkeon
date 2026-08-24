using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Exécuter" screen. Code-behind is XAML wiring only.</summary>
public partial class LaunchTabView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public LaunchTabView() => InitializeComponent();

    /// <summary>"Changer d'équipe" — lands on My teams (navigation is a window concern).</summary>
    private void OnCopyJournal(object sender, RoutedEventArgs e)
    {
        if (DataContext is LaunchTabViewModel { Log.CanCopy: true } tab)
        {
            try
            {
                // SetDataObject(copy: false) skips the flush that makes SetText throw when another
                // process (RDP, clipboard managers, VM tools) is holding the Win32 clipboard open.
                Clipboard.SetDataObject(tab.Log.BuildText(), copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    private void OnChangeTeam(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
            window.NavTeams.IsChecked = true;
    }

    /// <summary>The CLI-missing banner's escape hatch: open the Diagnostic screen.</summary>
    private void OnOpenDiagnostic(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
            window.NavDiag.IsChecked = true;
    }
}
