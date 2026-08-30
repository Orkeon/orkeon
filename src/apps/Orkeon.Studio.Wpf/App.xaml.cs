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

    /// <summary>
    /// The language the user explicitly picked, or null while nobody has. Every other
    /// preference write carries it, so a theme or mode toggle can never stamp a DETECTED
    /// language into the file and freeze it there.
    /// </summary>
    internal static string? ExplicitLanguage { get; private set; }

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

        // Presentation preferences are applied before the window exists so the first render is
        // already in the right theme and language. Reading is tolerant (defaults on any failure)
        // and a smoke run only ever reads — writes happen on user toggles, which a smoke never does.
        var preferences = UiPreferences.Load();
        ExplicitLanguage = preferences.Language;
        if (preferences.IsDark)
        {
            ThemeManager.Apply(dark: true);
        }

        // The language is NOT applied here any more: whether it comes from a stored choice
        // or from the machine is a decision with rules, and it belongs to the ViewModel that
        // owns them (T-13). It applies it as it resolves it, below.

        // The operator's CLI directory must be in force before any locator is built —
        // the ViewModel below wires the runner, the forge client and the run client.
        Orkeon.Studio.Core.Process.OrkeonBinaryLocator.DirectoryOverride = arguments.CliDirectory;

        // The ViewModel is built here, before the smoke switch is honoured — but building it only
        // wires the seams together. Everything that touches the machine (locating the co-installed
        // CLI, reading the history file) is deferred to InitializeAsync below, which a smoke run
        // never reaches; that is what makes the smoke independent of the environment.
        _viewModel = MainWindowViewModel.CreateForCurrentMachine(
            new WindowPathPicker(),
            new WpfDispatcher(Dispatcher),
            I18nStudioStrings.Instance,
            preferences.Mode,
            mode => UiPreferences.Save(ThemeManager.IsDark, ExplicitLanguage, mode),
            shellOpener: ShellOpener.Instance,
            // The assistant's beats are timed; the ViewModels only know how to ask for
            // "later", and this is the only place that knows what later means in WPF.
            delay: new WpfDelay(Dispatcher),
            // A stored language means the user picked it; null means nobody did, and the
            // machine decides again — which is what makes a change of Windows language
            // still get followed on the next start.
            initialLanguage: preferences.Language,
            persistLanguage: language =>
            {
                ExplicitLanguage = language;
                UiPreferences.Save(ThemeManager.IsDark, language, preferences.Mode ?? "novice");
            },
            applyLanguage: I18n.Instance.SetLanguage);

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

        if (arguments.CaptureScreensDirectory is { } captureDirectory)
        {
            // Screenshot campaign: initialize like a real session (teams, history, doctor all
            // populated from this machine), then walk every screen and leave. Exit code 0 with
            // the image count on stdout; any failure exits 1 with the reason on stderr.
            _ = Dispatcher.BeginInvoke(() => RunCaptureCampaignAsync(window, _viewModel, captureDirectory));

            return;
        }

        _ = _viewModel.InitializeAsync();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Top-level fault barrier of the headless campaign: any failure must " +
                        "become exit code 1 with a message, never a dead window.")]
    private async Task RunCaptureCampaignAsync(MainWindow window, MainWindowViewModel shell, string directory)
    {
        try
        {
            await shell.InitializeAsync();
            var count = await ScreenCaptureRunner.RunAsync(window, shell, directory);
            await Console.Out.WriteLineAsync($"capture-screens: {count} image(s) written to {directory}");
            window.Close();
            Shutdown(0);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"capture-screens failed: {exception.Message}");
            window.Close();
            Shutdown(1);
        }
    }
}
