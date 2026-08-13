using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Studio.Config.Cli;
using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Config.Views;
// Aliased under a distinct name: the bare name `Application` binds to the enclosing
// `Orkeon.Application` namespace here, and the alias marks the Terminal.Gui call sites.
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Config;

/// <summary>
/// Starts the interactive editor. This is the only place that touches the Terminal.Gui
/// application lifecycle — <c>--version</c> and <c>--help</c> never reach it, which is what
/// makes them work in a redirected shell.
/// </summary>
internal static class StudioConfigApp
{
    /// <summary>Exit code when the process has no terminal to draw on.</summary>
    public const int ExitNoTerminal = 1;

    /// <summary>Exit code when the editor could not be started.</summary>
    public const int ExitStartupFailure = 1;

    /// <summary>Opens the editor and returns the process exit code.</summary>
    public static int Run(StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TtyDetector.IsInteractiveTty())
        {
            Console.Error.WriteLine(
                "orkeon-studio-config needs an interactive terminal (its input or output is redirected here).");
            Console.Error.WriteLine("Run it from a terminal, or use --version / --help, which need none.");
            return ExitNoTerminal;
        }

        return RunInteractive(options);
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Top-level fault barrier of a terminal application: a driver or terminal failure " +
                        "must leave the console readable and return an exit code, not print a stack trace " +
                        "over a half-initialized screen.")]
    private static int RunInteractive(StartupOptions options)
    {
        var initialized = false;

        try
        {
            // Driver choice mirrors Orkeon.Cli.TerminalGui: the DOTNET driver is the one that
            // renders in WSL and container terminals, where the default ANSI driver stays blank.
            TerminalApp.Init(DriverName());
            initialized = true;

            var model = new ConfigEditorModel();
            var startupMessage = OpenStartupFile(model, options.SettingsPath);

            using var window = new StudioConfigWindow(model);
            if (startupMessage is not null)
                MessageListDialog.ShowInfo("Open", startupMessage);

            TerminalApp.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"orkeon-studio-config could not start: {ex.Message}"));
            return ExitStartupFailure;
        }
        finally
        {
            if (initialized)
                TerminalApp.Shutdown();
        }
    }

    private static string? OpenStartupFile(ConfigEditorModel model, string? settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
            return null;

        return model.OpenAsync(settingsPath).GetAwaiter().GetResult();
    }

    private static string DriverName()
    {
        var requested = Environment.GetEnvironmentVariable("TUI_DRIVER");
        if (!string.IsNullOrWhiteSpace(requested))
            return requested.Trim();

        return OperatingSystem.IsWindows()
            ? Terminal.Gui.Drivers.DriverRegistry.Names.WINDOWS
            : Terminal.Gui.Drivers.DriverRegistry.Names.DOTNET;
    }
}
