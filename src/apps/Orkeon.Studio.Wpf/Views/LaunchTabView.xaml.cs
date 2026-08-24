using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Exécuter" screen. Code-behind is XAML wiring only.</summary>
public partial class LaunchTabView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public LaunchTabView() => InitializeComponent();

    /// <summary>"Changer d'équipe" — lands on My teams (navigation is a window concern).</summary>
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
