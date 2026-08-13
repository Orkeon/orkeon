using Orkeon.Studio.Core;

namespace Orkeon.Studio.Run.Cli;

/// <summary>
/// Identity of the launcher as the command line reports it. The version comes from the
/// assembly the build stamped, so it can never drift from <c>src/Directory.Build.props</c>.
/// </summary>
internal static class StudioRunInfo
{
    /// <summary>Name of the command the installers put on PATH.</summary>
    public const string ToolName = "orkeon-studio-run";

    /// <summary>Version of the running assembly, without its build metadata.</summary>
    public static string Version { get; } = StudioAssemblyInfo.VersionOf(typeof(StudioRunInfo).Assembly);

    /// <summary>The single line <c>--version</c> writes.</summary>
    public static string VersionLine { get; } =
        StudioAssemblyInfo.VersionLine(ToolName, typeof(StudioRunInfo).Assembly);

    /// <summary>The text <c>--help</c> writes.</summary>
    public static string HelpText { get; } = string.Join(
        Environment.NewLine,
        VersionLine,
        string.Empty,
        "Terminal launcher for Orkeon crews. It does not run crews itself: it points the",
        "co-installed `orkeon` CLI at the crew you pick and streams its output.",
        string.Empty,
        "Usage:",
        $"  {ToolName}              Open the launcher (needs an interactive terminal).",
        $"  {ToolName} --version    Print the version and exit.",
        $"  {ToolName} --help       Print this help and exit.",
        string.Empty,
        "Inside the launcher:",
        "  Target        a .yaml/.yml or .ork.ts/.js file, or a crew directory; the shape is",
        "                detected and only the options that apply to it stay enabled.",
        "  Settings      'auto' lets the CLI's own resolution chain pick the appsettings.json,",
        "                or give an explicit path (--settings).",
        "  Mounts        launch-only mounts (--mount); each one replaces the appsettings mount",
        "                at the same index rather than merging with it.",
        "  Validate      a dry run (--validate): the crew is loaded strictly, nothing is kicked off.",
        "  Run / Esc     start the run, or ask the running process to stop (exit code 130).",
        string.Empty);
}
