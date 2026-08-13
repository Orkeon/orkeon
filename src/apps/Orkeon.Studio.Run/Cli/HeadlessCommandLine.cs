using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Orkeon.Studio.Run.Cli;

/// <summary>What a headless argument answers: the text to write, where to write it, and the exit code.</summary>
/// <param name="Text">Exactly what to write, newline included.</param>
/// <param name="ExitCode">Code the process returns.</param>
/// <param name="IsError">True when the text belongs on stderr rather than stdout.</param>
internal sealed record HeadlessResponse(string Text, int ExitCode, bool IsError = false);

/// <summary>
/// The arguments answered without a terminal.
/// <para>
/// This runs before <c>Application.Init</c> on purpose: <c>--version</c> and <c>--help</c>
/// are what the packaging smokes call, on machines with no TTY and with stdin/stdout
/// redirected. Initialising Terminal.Gui there would fail — so nothing may touch it until
/// this class has declined to handle the arguments.
/// </para>
/// </summary>
internal static class HeadlessCommandLine
{
    /// <summary>Exit code of an answered question: asking for the version is not an error.</summary>
    public const int SuccessExitCode = 0;

    /// <summary>Exit code of an argument the launcher does not know — same as `orkeon-studio-config`.</summary>
    public const int UsageExitCode = 2;

    /// <summary>Spellings that print the version.</summary>
    public static IReadOnlyList<string> VersionFlags { get; } = ["--version", "-v"];

    /// <summary>Spellings that print the help.</summary>
    public static IReadOnlyList<string> HelpFlags { get; } = ["--help", "-h", "-?"];

    /// <summary>
    /// Answers <paramref name="arguments"/> without a terminal, unless they are the bare
    /// invocation that opens the UI.
    /// </summary>
    /// <returns>
    /// True when <paramref name="response"/> is what the process should write and return;
    /// false only for an empty argument list, which opens the launcher.
    /// </returns>
    public static bool TryHandle(
        IReadOnlyList<string>? arguments,
        [NotNullWhen(true)] out HeadlessResponse? response)
    {
        var given = arguments ?? [];

        if (given.Any(argument => Matches(argument, VersionFlags)))
        {
            response = new HeadlessResponse(StudioRunInfo.VersionLine + Environment.NewLine, SuccessExitCode);
            return true;
        }

        if (given.Any(argument => Matches(argument, HelpFlags)))
        {
            response = new HeadlessResponse(StudioRunInfo.HelpText, SuccessExitCode);
            return true;
        }

        // An argument we do not know is a typo, and opening a full-screen UI on a typo hides
        // it. The launcher takes no arguments of its own: the crew is picked on screen.
        if (given.Count > 0)
        {
            response = new HeadlessResponse(UnknownArgumentText(given[0]), UsageExitCode, IsError: true);
            return true;
        }

        response = null;
        return false;
    }

    private static string UnknownArgumentText(string argument) => string.Join(
        Environment.NewLine,
        string.Create(CultureInfo.InvariantCulture, $"{StudioRunInfo.ToolName}: unknown argument '{argument}'."),
        $"It takes no arguments of its own — the crew is picked on screen. Try `{StudioRunInfo.ToolName} --help`.",
        string.Empty);

    private static bool Matches(string? argument, IReadOnlyList<string> flags) =>
        argument is not null && flags.Contains(argument.Trim(), StringComparer.OrdinalIgnoreCase);
}
