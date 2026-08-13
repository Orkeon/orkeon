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

        var arguments = StartupArguments.Parse(e.Args);

        if (arguments.Unrecognized.Count > 0)
        {
            // Refused before anything is built: an unknown switch is a mistake to report, not one
            // to open a window over. Reported on stderr rather than in a message box — this path
            // has to stay non-blocking for a script or a CI runner that mistyped a flag.
            Console.Error.WriteLine(StartupArguments.DescribeUnrecognized(arguments.Unrecognized));
            Shutdown(StartupArguments.UnrecognizedArgumentExitCode);
            return;
        }

        // The ViewModel is built here, before the smoke switch is honoured — but building it only
        // wires the seams together. Everything that touches the machine (locating the co-installed
        // CLI, reading the history file) is deferred to InitializeAsync below, which a smoke run
        // never reaches; that is what makes the smoke independent of the environment.
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
