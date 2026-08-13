using System.Text;

namespace Orkeon.Studio.Core.Launch;

/// <summary>Quoting dialect used to render a command line for a human.</summary>
public enum CommandLineQuotingStyle
{
    /// <summary>Windows quoting on Windows, POSIX quoting elsewhere.</summary>
    Auto,

    /// <summary>POSIX shell quoting (single quotes).</summary>
    Posix,

    /// <summary>Windows command-line quoting (double quotes, backslash escapes).</summary>
    Windows,
}

/// <summary>
/// Renders an argument list as the command line a user would type. Display only: the
/// process runner passes the argument list itself, so nothing is ever quoted twice.
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
            ? QuoteWindows(argument)
            : QuotePosix(argument);
    }

    private static CommandLineQuotingStyle Resolve(CommandLineQuotingStyle style) =>
        style != CommandLineQuotingStyle.Auto
            ? style
            : OperatingSystem.IsWindows() ? CommandLineQuotingStyle.Windows : CommandLineQuotingStyle.Posix;

    private static string QuotePosix(string argument)
    {
        if (argument.Length > 0 && argument.All(IsPosixSafe))
            return argument;

        // Single quotes suppress every expansion; a literal quote closes, escapes, reopens.
        return "'" + argument.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static bool IsPosixSafe(char character) =>
        char.IsAsciiLetterOrDigit(character) || PosixSafeCharacters.Contains(character, StringComparison.Ordinal);

    private static string QuoteWindows(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '\n' or '\v' or '"'))
            return argument;

        var builder = new StringBuilder("\"");

        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                // Trailing backslashes must not escape the closing quote.
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(argument[i]);
            }
        }

        return builder.Append('"').ToString();
    }
}
