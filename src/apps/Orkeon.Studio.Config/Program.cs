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

    private static int Main(string[] args)
    {
        var options = StartupOptions.Parse(args);

        if (options.Error is { } error)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(StartupOptions.UsageLine);
            return ExitUsage;
        }

        if (options.ShowVersion)
        {
            Console.Out.WriteLine(StudioVersion.Line);
            return 0;
        }

        if (options.ShowHelp)
        {
            Console.Out.WriteLine(StartupOptions.HelpText);
            return 0;
        }

        return StudioConfigApp.Run(options);
    }
}
