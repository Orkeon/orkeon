using System.Globalization;

namespace Orkeon.Studio.Config.Cli;

/// <summary>
/// The command line of <c>orkeon-studio-config</c>. Parsing is separated from the UI on
/// purpose: <c>--version</c> and <c>--help</c> must answer on stdout with exit code 0 in a
/// redirected, TTY-less shell (the onboarding smokes run exactly that), so they are
/// resolved before Terminal.Gui is ever touched.
/// </summary>
internal sealed record StartupOptions
{
    /// <summary>Print the version and exit.</summary>
    public bool ShowVersion { get; init; }

    /// <summary>Print the usage text and exit.</summary>
    public bool ShowHelp { get; init; }

    /// <summary>Settings file to open at startup, when one was given.</summary>
    public string? SettingsPath { get; init; }

    /// <summary>Why the command line was rejected; null when it parsed.</summary>
    public string? Error { get; init; }

    /// <summary>True when the process must not start the interactive UI.</summary>
    public bool IsHeadless => ShowVersion || ShowHelp || Error is not null;

    /// <summary>One-line synopsis, printed with every error.</summary>
    public const string UsageLine = "Usage: orkeon-studio-config [--settings <path>] [--version] [--help]";

    /// <summary>The full <c>--help</c> text.</summary>
    public static string HelpText { get; } = string.Join(
        Environment.NewLine,
        "orkeon-studio-config — Orkeon Studio, appsettings editor (Terminal.Gui).",
        "",
        UsageLine,
        "",
        "Arguments:",
        "  <path>                    appsettings.json to open at startup (same as --settings).",
        "",
        "Options:",
        "  -s, --settings <path>     appsettings.json to open at startup.",
        "  -v, --version             Print the version and exit.",
        "  -h, --help                Print this help and exit.",
        "",
        "The editor writes the same JSON `orkeon init` writes, at the same locations",
        "`orkeon run` resolves. It needs an interactive terminal; --version and --help",
        "do not.");

    /// <summary>Parses the raw command line. Never throws: a bad argument becomes <see cref="Error"/>.</summary>
    public static StartupOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
            return new StartupOptions();

        var showVersion = false;
        var showHelp = false;
        string? settingsPath = null;

        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];

            switch (argument)
            {
                case "--version" or "-v":
                    showVersion = true;
                    continue;

                case "--help" or "-h" or "-?":
                    showHelp = true;
                    continue;

                case "--settings" or "-s":
                    if (i + 1 >= args.Count)
                        return Failure("Option '--settings' requires a path.");

                    settingsPath = args[++i];
                    continue;
            }

            if (argument.StartsWith('-'))
            {
                return Failure(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unknown option '{argument}'."));
            }

            if (settingsPath is not null)
            {
                return Failure(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unexpected argument '{argument}': a single settings file can be opened at startup."));
            }

            settingsPath = argument;
        }

        return new StartupOptions
        {
            ShowVersion = showVersion,
            ShowHelp = showHelp,
            SettingsPath = settingsPath,
        };
    }

    private static StartupOptions Failure(string message) => new() { Error = message };
}
