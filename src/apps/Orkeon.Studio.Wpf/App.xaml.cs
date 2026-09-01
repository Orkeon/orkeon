using System.Globalization;
using System.Windows;
using Orkeon.Studio.Wpf.Services;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
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
    /// The language the user explicitly picked, or null while nobody has — read from the
    /// ViewModel that owns the distinction rather than mirrored in a second field. Every
    /// preference write carries it, so a theme or mode toggle can never stamp a DETECTED
    /// language into the file and freeze it there.
    /// </summary>
    private string? ChosenLanguage =>
        _viewModel?.Language.IsExplicitChoice == true ? _viewModel.Language.Current : null;

    /// <summary>
    /// Says a fault out loud instead of letting it disappear. The window stays up — one broken
    /// gesture is not a reason to lose the work in progress — but the failure is on stderr for a
    /// terminal or CI run and in front of the user, who would otherwise be left clicking a
    /// control that has silently stopped answering.
    /// </summary>
    private static void ReportFault(Exception exception)
    {
        var detail = exception.ToString();
        Console.Error.WriteLine(detail);
        MessageBox.Show(
            string.Create(CultureInfo.CurrentCulture, $"{exception.GetType().Name}\n\n{exception.Message}"),
            "Orkeon Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnStartup(e);

        // A command that throws must never again be a button that quietly stops working.
        // WPF calls ICommand.Execute and ignores what it returns, so an async command body's
        // exception has nowhere to go on its own; this hands it to the dispatcher, where the
        // handler below reports it.
        AsyncRelayCommand.FaultHandler = ex => Dispatcher.BeginInvoke(new Action(() => ReportFault(ex)));
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ReportFault(args.Exception);
        };

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

        if (arguments.CaptureScreensDirectory is { } captureDirectory)
        {
            // The campaign owns its own windows, its own worlds and its own appearance, so it runs
            // ABOVE everything below: no stored preference is read, no --cli-dir override is
            // honoured, and no shell over the operator's real disk is ever built. That is what
            // makes the collection the same on any machine — and what makes it impossible for a
            // screenshot run to touch the operator's teams, history or settings.
            //
            // OnExplicitShutdown is load-bearing: the default closes the process the moment the
            // first pass's window closes, which would ship a campaign that silently produces one
            // pass out of eight.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // RenderTargetBitmap always rasterises in software; forcing the on-screen pipeline to
            // match removes a real source of machine-to-machine variation, because a drop shadow
            // does not render the same at hardware tier 2 and tier 0.
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;

            _ = Dispatcher.BeginInvoke(() => RunCaptureCampaignAsync(captureDirectory));

            return;
        }

        // Presentation preferences are applied before the window exists so the first render is
        // already in the right theme and language. Reading is tolerant (defaults on any failure)
        // and a smoke run only ever reads — writes happen on user toggles, which a smoke never does.
        var preferences = UiPreferences.Load();
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
            mode => UiPreferences.Save(ThemeManager.IsDark, ChosenLanguage, mode),
            shellOpener: ShellOpener.Instance,
            // The assistant's beats are timed; the ViewModels only know how to ask for
            // "later", and this is the only place that knows what later means in WPF.
            delay: new WpfDelay(Dispatcher),
            // A stored language means the user picked it; null means nobody did, and the
            // machine decides again — which is what makes a change of Windows language
            // still get followed on the next start.
            initialLanguage: preferences.Language,
            persistLanguage: language => UiPreferences.Save(
                ThemeManager.IsDark,
                language,
                // The mode is read LIVE, never from the startup snapshot: switching to Expert
                // and then picking a language would otherwise write the mode back to whatever
                // it was when the app opened, silently undoing the switch. Both lambdas only
                // ever run on a user gesture, long after _viewModel is assigned.
                _viewModel?.Mode.Mode ?? preferences.Mode ?? UiModeViewModel.Novice),
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

        _ = _viewModel.InitializeAsync();
    }

    /// <summary>
    /// The headless campaign, end to end. A stop that fails is reported and the walk continues — a
    /// campaign that half-works is far more useful than one that dies at image twelve — but the
    /// process still exits 1, with every failed stop named on stderr.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Top-level fault barrier of the headless campaign: any failure must " +
                        "become exit code 1 with a message, never a dead process.")]
    private async Task RunCaptureCampaignAsync(string directory)
    {
        try
        {
            var report = await Services.Capture.CaptureCampaign.RunAsync(
                new Services.Capture.CaptureOptions { Directory = directory });

            await Console.Out.WriteLineAsync(
                $"capture-screens: {report.Written}/{report.Planned} image(s) written to {directory}");

            if (report.Failures.Count > 0)
            {
                await Console.Error.WriteLineAsync(
                    $"capture-screens: {report.Failures.Count} stop(s) failed "
                    + $"(the seeded worlds were at {report.SandboxRoot}):");
                foreach (var failure in report.Failures)
                    await Console.Error.WriteLineAsync($"  {failure.Pass} / {failure.Stop}: {failure.Reason}");
            }

            Shutdown(report.Failures.Count == 0 ? 0 : 1);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"capture-screens failed: {exception}");
            Shutdown(1);
        }
    }
}
