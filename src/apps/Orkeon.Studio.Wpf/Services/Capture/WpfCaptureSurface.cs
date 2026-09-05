using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Orkeon.Studio.Wpf.ViewModels.Capture;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.Views;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>
/// The whole WPF half of the campaign's vocabulary: which radio button a screen is, where the
/// splash lives, how the tour opens, and what "settled" means.
/// <para>
/// Everything else — what to arrange, why, and what to assert about it — lives in the catalogue,
/// which compiles into the net10.0 test assembly and is checked on Linux. This class is the part
/// that cannot be, and it is deliberately the only one.
/// </para>
/// </summary>
internal sealed class WpfCaptureSurface(MainWindow window, MainWindowViewModel shell) : ICaptureSurface
{
    /// <summary>The window this surface drives, for the executor's own rendering.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public MainWindow Window { get; } = window;

    /// <inheritdoc />
    public async Task ShowAsync(CaptureScreen screen)
    {
        switch (screen)
        {
            case CaptureScreen.Create: Window.NavCreate.IsChecked = true; break;
            case CaptureScreen.Teams: Window.NavTeams.IsChecked = true; break;
            case CaptureScreen.Import: Window.NavImport.IsChecked = true; break;
            case CaptureScreen.Test: Window.NavTest.IsChecked = true; break;
            case CaptureScreen.Run: Window.NavRun.IsChecked = true; break;
            case CaptureScreen.History: Window.NavHistory.IsChecked = true; break;
            case CaptureScreen.Diagnostic: Window.NavDiag.IsChecked = true; break;

            case CaptureScreen.SettingsModel: ShowSettings(shell.Settings.ShowModelCommand); break;
            case CaptureScreen.SettingsFolders: ShowSettings(shell.Settings.ShowFoldersCommand); break;
            case CaptureScreen.SettingsLimits: ShowSettings(shell.Settings.ShowLimitsCommand); break;
            case CaptureScreen.SettingsJson: ShowSettings(shell.Settings.ShowJsonCommand); break;

            default: throw new ArgumentOutOfRangeException(nameof(screen), screen, "unknown capture screen");
        }

        await SettleAsync();
    }

    /// <summary>The panel a stop's screen should have brought forward, so the shot can be checked.</summary>
    public FrameworkElement? PanelFor(CaptureScreen screen) => screen switch
    {
        CaptureScreen.Create => Window.CreatePanel,
        CaptureScreen.Teams => Window.TeamsPanel,
        CaptureScreen.Import => Window.ImportPanel,
        CaptureScreen.Test => Window.TestPanel,
        CaptureScreen.Run => Window.RunPanel,
        CaptureScreen.History => Window.HistoryPanel,
        CaptureScreen.Diagnostic => Window.DiagPanel,
        _ => Window.SettingsPanel,
    };

    /// <inheritdoc />
    public void ShowSplash() => Window.PoseSplashForCapture();

    /// <inheritdoc />
    public void HideSplash() => Window.HideSplashForCapture();

    /// <inheritdoc />
    public void StartTourAt(int stepIndex) => Window.Tour.Start(MainWindow.TourSteps, stepIndex);

    /// <inheritdoc />
    public void EndTour() => Window.Tour.End();

    /// <inheritdoc />
    public async Task SettleAsync() => await CaptureSettle.SettleAsync(Window);

    private void ShowSettings(ViewModels.Mvvm.RelayCommand command)
    {
        Window.NavSettings.IsChecked = true;
        command.Execute(null);
    }
}
