using System.Windows;
using Orkeon.Studio.Wpf.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.Views;

namespace Orkeon.Studio.Wpf;

/// <summary>
/// The application entry point. Its only job is to read the command line, build the window's
/// ViewModel and show it; everything the window can do lives in the ViewModels, which is what lets
/// the tests cover the behaviour without a WPF <see cref="System.Windows.Application"/>.
/// </summary>
public partial class App : System.Windows.Application
{
    private MainWindowViewModel? _viewModel;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnStartup(e);

        // The startup switch is handled before anything else is built, so a smoke run never depends
        // on the disk probes or on the co-installed CLI being present.
        var arguments = StartupArguments.Parse(e.Args);

        _viewModel = MainWindowViewModel.CreateForCurrentMachine(
            new WindowPathPicker(),
            new WpfDispatcher(Dispatcher));

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();

        if (arguments.SmokeExit)
        {
            // Smoke mode (spec §8.4): prove the window opens and its bindings resolve, then leave.
            // The shutdown is queued rather than immediate so the first render completes first — a
            // binding failure has to be able to surface before the process exits.
            _ = Dispatcher.BeginInvoke(() =>
            {
                window.Close();
                Shutdown(0);
            });

            return;
        }

        _ = _viewModel.InitializeAsync();
    }
}
