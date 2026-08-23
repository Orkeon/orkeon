using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Créer une équipe" wizard. XAML wiring only; every behaviour lives in the ViewModel.</summary>
public partial class CreateTeamView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public CreateTeamView() => InitializeComponent();

    /// <summary>Folds/unfolds the technical journal (pure presentation state on the VM).</summary>
    private void OnToggleTech(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Teams.CreateTeamViewModel vm)
            vm.IsTechOpen = !vm.IsTechOpen;
    }
}
