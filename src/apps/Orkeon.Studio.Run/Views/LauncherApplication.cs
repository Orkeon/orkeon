using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Studio.Run.Launcher;
using Terminal.Gui.App;
using TerminalApp = Terminal.Gui.App.Application;

namespace Orkeon.Studio.Run.Views;

/// <summary>
/// Owns the Terminal.Gui lifecycle of the launcher: pick a driver, run the one window,
/// shut the application down whatever happened.
/// </summary>
internal static class LauncherApplication
{
    /// <summary>Exit code returned when the terminal could not host the UI at all.</summary>
    internal const int TerminalUnavailableExitCode = 1;

    /// <summary>Opens the launcher and returns the process exit code.</summary>
    internal static int Run(RunLauncherViewModel? viewModel = null)
    {
        // Checked before Init rather than left to it: a redirected stdin is not an error the
        // driver reliably raises, and a launcher that renders a full screen into a pipe (or
        // waits forever for a keystroke that cannot come) is worse than one that says so.
        if (!TtyDetector.IsInteractiveTty())
        {
            ReportNoTerminal("this process has no interactive terminal (input or output is redirected, or TERM is unset).");
            return TerminalUnavailableExitCode;
        }

        var launcher = viewModel ?? RunLauncherViewModel.ForCurrentMachine();

        try
        {
            TerminalApp.Init(DriverName());
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            ReportNoTerminal(ex.Message);
            return TerminalUnavailableExitCode;
        }

        try
        {
            using var window = new RunLauncherWindow(launcher);
            TerminalApp.Run(window, errorHandler: null);
            return window.ExitCode;
        }
        finally
        {
            TerminalApp.Shutdown();
        }
    }

    /// <summary>Says why the UI cannot open, and what to do instead.</summary>
    private static void ReportNoTerminal(string reason)
    {
        System.Console.Error.WriteLine($"{Cli.StudioRunInfo.ToolName} needs an interactive terminal: {reason}");
        System.Console.Error.WriteLine(
            $"Run `{Cli.StudioRunInfo.ToolName} --help` for what it does, or launch crews with `orkeon run` directly.");
    }

    /// <summary>
    /// The driver to initialise. Same choice as <c>Orkeon.Cli.TerminalGui</c>: the default
    /// ANSI driver draws nothing under WSL and in many container terminals, so Linux and
    /// macOS get the System.Console-based one and Windows its native driver.
    /// </summary>
    private static string DriverName()
    {
        var requested = Environment.GetEnvironmentVariable("TUI_DRIVER");
        if (!string.IsNullOrWhiteSpace(requested))
        {
#pragma warning disable CA1308 // the driver registry keys are lowercase; this is not a comparison normalization
            return requested.ToLowerInvariant();
#pragma warning restore CA1308
        }

        return OperatingSystem.IsWindows()
            ? Terminal.Gui.Drivers.DriverRegistry.Names.WINDOWS
            : Terminal.Gui.Drivers.DriverRegistry.Names.DOTNET;
    }
}
