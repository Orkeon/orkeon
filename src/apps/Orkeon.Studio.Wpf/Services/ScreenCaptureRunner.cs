using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.Views;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// The screenshot campaign behind <c>orkeon-studio --capture-screens &lt;dir&gt;</c>: walks every
/// screen of the window in both modes (plus the overlays), saves one PNG per stop, and exits.
/// The collection is the fidelity-remediation reference against the v3 design mock — one command
/// on a Windows machine instead of a by-hand tour.
/// </summary>
[SuppressVfsCompliance(
    "UI-layer capture harness writing PNGs where the operator pointed it — Studio's own state, " +
    "not framework I/O; same exception class as UiPreferences.")]
public static class ScreenCaptureRunner
{
    /// <summary>Runs the campaign and returns the number of images written.</summary>
    public static async Task<int> RunAsync(MainWindow window, MainWindowViewModel shell, string directory)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var index = 0;

        // The startup plate first — it is a screen of the design too.
        await SettleAsync(window);
        Save(window, directory, ++index, "any", "splash");
        window.SkipSplashForCapture();

        foreach (var (mode, isExpert) in new[] { ("novice", false), ("expert", true) })
        {
            if (isExpert)
                shell.Mode.SetExpertCommand.Execute(null);
            else
                shell.Mode.SetNoviceCommand.Execute(null);

            foreach (var (name, check) in Stops(window, shell, isExpert))
            {
                check();
                await SettleAsync(window);
                Save(window, directory, ++index, mode, name);
            }
        }

        // Overlays, captured once in expert mode (every element visible).
        window.NavSettings.IsChecked = true;
        shell.Settings.ShowModelCommand.Execute(null);
        shell.Settings.Profiles.NewProfileCommand.Execute(null);
        var editor = shell.Settings.Profiles.Editor;
        if (editor is not null)
        {
            editor.SelectedProvider = editor.Providers.FirstOrDefault(p => p.Name == LlmPresets.DeepSeek);
            await SettleAsync(window);
            Save(window, directory, ++index, "expert", "reglage-editeur");
            editor.CancelCommand.Execute(null);
        }

        shell.About.OpenCommand.Execute(null);
        await SettleAsync(window);
        Save(window, directory, ++index, "expert", "a-propos");
        shell.About.CloseCommand.Execute(null);

        return index;
    }

    private static IEnumerable<(string Name, Action Check)> Stops(
        MainWindow window, MainWindowViewModel shell, bool isExpert)
    {
        yield return ("creer-equipe", () => window.NavCreate.IsChecked = true);
        yield return ("mes-equipes", () => window.NavTeams.IsChecked = true);
        yield return ("importer", () => window.NavImport.IsChecked = true);
        if (isExpert)
            yield return ("tester", () => window.NavTest.IsChecked = true);
        yield return ("executer", () => window.NavRun.IsChecked = true);
        yield return ("historique", () => window.NavHistory.IsChecked = true);
        yield return ("reglages-modele", () =>
        {
            window.NavSettings.IsChecked = true;
            shell.Settings.ShowModelCommand.Execute(null);
        });
        yield return ("reglages-dossiers", () => shell.Settings.ShowFoldersCommand.Execute(null));
        if (isExpert)
        {
            yield return ("reglages-limites", () => shell.Settings.ShowLimitsCommand.Execute(null));
            yield return ("reglages-json", () => shell.Settings.ShowJsonCommand.Execute(null));
        }

        yield return ("diagnostic", () => window.NavDiag.IsChecked = true);
    }

    /// <summary>Two idle passes so bindings, layout and render all catch up before the shot.</summary>
    private static async Task SettleAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(120);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void Save(Window window, string directory, int index, string mode, string name)
    {
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        if (width <= 0 || height <= 0)
            return;

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var path = Path.Combine(directory, $"{index:D2}-{mode}-{name}.png");
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
