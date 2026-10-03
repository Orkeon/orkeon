using System.Text;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Studio.Core.Launch;

/// <summary>Quoting dialect used to render a command line for a human.</summary>
public enum CommandLineQuotingStyle
{
    /// <summary>Windows quoting on Windows, POSIX quoting elsewhere.</summary>
    Auto,

    /// <summary>POSIX shell quoting (single quotes).</summary>
    Posix,

    /// <summary>
    /// A line for the <c>cmd</c> prompt: the C runtime's quoting, then <c>cmd</c>'s — carets and
    /// percents (STUDIO-51). PowerShell reads such a line otherwise.
    /// </summary>
    Windows,
}

/// <summary>
/// Renders an argument list as the command line a user would type. Display only: the
/// process runner passes the argument list itself, so nothing is ever quoted twice.
/// <para>
/// « Copy the command » pastes it into a terminal: under Windows a line for the <c>cmd</c> prompt,
/// each argument written by the composer of the team launchers
/// (<see cref="TeamLauncherScript.QuoteForCmdPrompt"/>, STUDIO-51) — a value with <c>&amp;</c> or
/// <c>%PATH%</c> reaches the run as it is, a line break as a space —; elsewhere a line for
/// <c>sh</c>, between single quotes.
/// </para>
/// </summary>
public static class CommandLineDisplay
{
    /// <summary>Name of the co-installed CLI.</summary>
    public const string DefaultExecutable = "orkeon";

    private const string PosixSafeCharacters = "-_@%+=:,./";

    /// <summary>Joins an executable and its arguments into a copy-pasteable command line.</summary>
    public static string Format(
        IReadOnlyList<string> arguments,
        string executable = DefaultExecutable,
        CommandLineQuotingStyle style = CommandLineQuotingStyle.Auto)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(executable);

        var resolved = Resolve(style);
        var builder = new StringBuilder(Quote(executable, resolved));

        foreach (var argument in arguments)
            builder.Append(' ').Append(Quote(argument, resolved));

        return builder.ToString();
    }

    /// <summary>Quotes one argument for the given shell dialect.</summary>
    public static string Quote(string argument, CommandLineQuotingStyle style = CommandLineQuotingStyle.Auto)
    {
        ArgumentNullException.ThrowIfNull(argument);

        return Resolve(style) == CommandLineQuotingStyle.Windows
            ? TeamLauncherScript.QuoteForCmdPrompt(argument)
            : QuotePosix(argument);
    }

    private static CommandLineQuotingStyle Resolve(CommandLineQuotingStyle style)
    {
        if (style != CommandLineQuotingStyle.Auto)
            return style;

        return OperatingSystem.IsWindows() ? CommandLineQuotingStyle.Windows : CommandLineQuotingStyle.Posix;
    }

    private static string QuotePosix(string argument)
    {
        if (argument.Length > 0 && argument.All(IsPosixSafe))
            return argument;

        // Single quotes suppress every expansion; a literal quote closes, escapes, reopens.
        return TeamLauncherScript.QuoteForPosixShell(argument);
    }

    private static bool IsPosixSafe(char character) =>
        char.IsAsciiLetterOrDigit(character) || PosixSafeCharacters.Contains(character, StringComparison.Ordinal);
}
