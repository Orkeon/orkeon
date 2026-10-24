using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Config.Cli;

namespace Orkeon.Studio.Config;

/// <summary>
/// Entry point of <c>orkeon-studio-config</c>.
/// <para>
/// The command line is answered before anything touches Terminal.Gui: the onboarding
/// smokes run <c>orkeon-studio-config --version</c> with stdin and stdout redirected, where
/// initializing a console driver would fail. Only a run that actually opens the editor
/// reaches <see cref="StudioConfigApp"/>.
/// </para>
/// </summary>
internal static class Program
{
    /// <summary>Exit code of a command line that could not be parsed.</summary>
    public const int ExitUsage = 2;

    [SuppressConsoleCompliance("CONSOLE-SINK: the one place orkeon-studio-config hands the real streams on.")]
    private static int Main(string[] args)
        => Run(args, Console.Out, Console.Error, SystemTerminalProbe.Instance);

    /// <summary>Answers the command line on the given streams and returns the exit code.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error, ITerminalProbe terminal)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var options = StartupOptions.Parse(args);

        if (options.Error is { } parseError)
        {
            error.WriteLine(parseError);
            error.WriteLine(StartupOptions.UsageLine);
            return ExitUsage;
        }

        if (options.ShowVersion)
        {
            output.WriteLine(StudioVersion.Line);
            return 0;
        }

        if (options.ShowHelp)
        {
            output.WriteLine(StartupOptions.HelpText);
            return 0;
        }

        return new StudioConfigApp(error, terminal).Run(options);
    }
}
