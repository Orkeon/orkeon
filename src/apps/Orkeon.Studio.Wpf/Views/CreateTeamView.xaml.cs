using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The create-a-team wizard. XAML wiring only; every behaviour lives in the ViewModel.</summary>
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

    /// <summary>
    /// STUDIO-13: copies the failure card's report — command line, exit code, engine error,
    /// the whole stderr — then shows "Copied!" for ~1.6 s, the diagnostic screen's pattern.
    /// </summary>
    private void OnCopyFailureReport(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateTeamViewModel { CanCopyFailureReport: true } wizard)
        {
            try
            {
                Clipboard.SetDataObject(wizard.BuildFailureReport(), copy: false);

                wizard.FailureReportCopied = true;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                timer.Tick += (_, _) => { wizard.FailureReportCopied = false; timer.Stop(); };
                timer.Start();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The clipboard stayed busy: nothing to surface, the button can be pressed again.
            }
        }
    }

    /// <summary>Copies the rendered crew YAML of the generated-definition card.</summary>
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
