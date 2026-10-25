using System.Windows;
using System.Windows.Controls;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Views;

/// <summary>The "Diagnostic" screen. XAML wiring only; every behaviour lives in the ViewModel.</summary>
public partial class DiagnosticView : UserControl
{
    /// <summary>Loads the XAML.</summary>
    public DiagnosticView() => InitializeComponent();

    // Lot 5: each problem opens the section that fixes it - Models, Allowed folders, the File -, or
    // tests the model's connection there, or runs the checks again when the fix is not Studio's.
    private void OnFixCheck(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DoctorCheckViewModel check })
            return;

        switch (check.FixKind)
        {
            case DoctorCheckViewModel.FixModels:
                OpenSettings(SettingsScreenViewModel.ModelTab);
                break;
            case DoctorCheckViewModel.FixFolders:
                OpenSettings(SettingsScreenViewModel.FoldersTab);
                break;
            case DoctorCheckViewModel.FixFile:
                OpenSettings(SettingsScreenViewModel.JsonTab);
                break;
            case DoctorCheckViewModel.FixTest:
                if (OpenSettings(SettingsScreenViewModel.ModelTab) is { } settings)
                    TestDefaultModel(settings.Profiles);
                break;
            default:
                if (DataContext is DiagnosticViewModel diagnostic && diagnostic.RunCommand.CanExecute(null))
                    diagnostic.RunCommand.Execute(null);
                break;
        }
    }

    // Opens the default model's editor (the first one when none is marked default) and tests it.
    private static void TestDefaultModel(ViewModels.Config.ModelProfilesViewModel profiles)
    {
        if (profiles.Editor is null)
        {
            var item = profiles.Profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.Profiles.FirstOrDefault();
            if (item is null)
                return;
            item.EditCommand.Execute(null);
        }

        if (profiles.Editor?.TestConnectionCommand is { } test && test.CanExecute(null))
            test.Execute(null);
    }

    private void OnOpenSettingsFile(object sender, RoutedEventArgs e) => OpenSettings(SettingsScreenViewModel.JsonTab);

    private SettingsScreenViewModel? OpenSettings(string tab)
    {
        if (Window.GetWindow(this) is not MainWindow { DataContext: ViewModels.Shell.MainWindowViewModel shell } window)
            return null;

        shell.Settings.ShowSection(tab);
        window.NavSettings.IsChecked = true;
        return shell.Settings;
    }

    /// <summary>Copies the last report to the clipboard — presentation-only, no ViewModel involvement.</summary>
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
