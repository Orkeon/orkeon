using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Run.Cli;
using Orkeon.Studio.Run.Views;

namespace Orkeon.Studio.Run;

/// <summary>Entry point of <c>orkeon-studio-run</c>.</summary>
internal static class Program
{
    [SuppressConsoleCompliance("CONSOLE-SINK: the one place orkeon-studio-run hands the real streams on.")]
    private static int Main(string[] args)
        => Run(args, System.Console.Out, System.Console.Error, SystemTerminalProbe.Instance);

    /// <summary>Answers the command line on the given streams, or opens the launcher.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error, ITerminalProbe terminal)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        // Arguments are answered first, deliberately: --version and --help must work with no
        // terminal at all (that is what the packaging smokes call), and Application.Init would
        // fail there. Nothing above this line touches Terminal.Gui.
        if (HeadlessCommandLine.TryHandle(args, out var response))
        {
            var writer = response.IsError ? error : output;
            writer.Write(response.Text);
            writer.Flush();
            return response.ExitCode;
        }

        return new LauncherApplication(error, terminal).Run();
    }
}
