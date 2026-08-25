using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Créer une équipe" wizard. XAML wiring only; every behaviour lives in the ViewModel.</summary>
public partial class CreateTeamView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public CreateTeamView() => InitializeComponent();

    /// <summary>Folds/unfolds the technical journal (pure presentation state on the VM).</summary>
    private void OnCopyTechJournal(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateTeamViewModel { RawLog.CanCopy: true } wizard)
        {
            try
            {
                // SetDataObject(copy: false) skips the flush that makes SetText throw when another
                // process (RDP, clipboard managers, VM tools) is holding the Win32 clipboard open.
                Clipboard.SetDataObject(wizard.RawLog.BuildText(), copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    /// <summary>Copies the rendered crew YAML of the «Définition générée» card.</summary>
    private void OnCopyDefinition(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateTeamViewModel { HasCrewDefinition: true } wizard)
        {
            try
            {
                Clipboard.SetDataObject(wizard.CrewDefinitionYaml, copy: false);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    private void OnToggleTech(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.Teams.CreateTeamViewModel vm)
            vm.IsTechOpen = !vm.IsTechOpen;
    }
}
