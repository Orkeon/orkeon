using Orkeon.Studio.Run.Cli;
using Orkeon.Studio.Run.Views;

namespace Orkeon.Studio.Run;

/// <summary>Entry point of <c>orkeon-studio-run</c>.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Arguments are answered first, deliberately: --version and --help must work with no
        // terminal at all (that is what the packaging smokes call), and Application.Init would
        // fail there. Nothing above this line touches Terminal.Gui.
        if (HeadlessCommandLine.TryHandle(args, out var response))
        {
            var writer = response.IsError ? System.Console.Error : System.Console.Out;
            writer.Write(response.Text);
            writer.Flush();
            return response.ExitCode;
        }

        return LauncherApplication.Run();
    }
}
