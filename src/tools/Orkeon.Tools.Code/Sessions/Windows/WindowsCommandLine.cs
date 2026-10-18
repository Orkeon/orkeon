using System.Text;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// The command line <c>CreateProcessW</c> takes, built from an argument vector. Pure.
/// </summary>
/// <remarks>
/// <para>
/// A Windows program receives one string and splits it itself; the C runtime and
/// <c>CommandLineToArgvW</c> share the rules <see cref="Quote"/> writes for: an argument without
/// space, tab or quote goes as it is; any other is quoted, a quote inside it is preceded by a
/// backslash, and the backslashes that come before a quote (or before the closing quote) are doubled.
/// </para>
/// <para>
/// <b>cmd.exe built-ins.</b> <c>echo</c>, <c>dir</c>, <c>type</c>, … exist only inside
/// <c>cmd.exe</c>, which does NOT follow those rules: it reads the line itself, and outside double
/// quotes <c>&amp; | &lt; &gt; ^ ( )</c> are operators. <see cref="CmdBuiltin"/> therefore writes
/// <c>cmd.exe /d /v:off /s /c "&lt;builtin&gt; &lt;arguments&gt;"</c> where every character of an
/// argument that is not a plain word sits between double quotes:
/// </para>
/// <list type="bullet">
/// <item>an argument made only of letters, digits and <c>. _ - + / \ : ~ # @ [ ] { } * ? ' $</c>
/// goes bare (none of them is a cmd operator);</item>
/// <item>any other is wrapped in double quotes, and each double quote inside it is doubled: cmd
/// toggles its quoted state on every quote, so a doubled quote closes and reopens at once, with
/// nothing outside the quotes in between;</item>
/// <item><c>%</c> is refused (cmd expands <c>%NAME%</c> even between quotes; the tool already refuses
/// it for a built-in), as are line breaks and NUL; <c>/v:off</c> keeps <c>!NAME!</c> literal;
/// <c>/d</c> skips the AutoRun commands of the registry.</item>
/// </list>
/// Hence no character of an argument ever reaches cmd's parser outside double quotes unless it is a
/// plain word character. A built-in still sees the quotes as cmd hands them over (<c>echo "a b"</c>
/// prints the quotes), exactly as before.
/// </remarks>
internal static class WindowsCommandLine
{
    /// <summary>The cmd.exe switches put before <c>/c</c>.</summary>
    public const string CmdSwitches = "/d /v:off /s /c";

    /// <summary>One argument quoted so that the C runtime reads it back unchanged.</summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var builder = new StringBuilder(argument.Length + 2).Append('"');
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
                // Before the closing quote: every backslash doubled.
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            else
                builder.Append('\\', backslashes).Append(argument[i]);
        }

        return builder.Append('"').ToString();
    }

    /// <summary>
    /// The full command line of a program: its path between quotes (the program name is not parsed
    /// with the backslash rules, and a path holds no quote), then each argument quoted.
    /// </summary>
    public static string Build(string executablePath, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        if (executablePath.Contains('"', StringComparison.Ordinal))
            throw new ArgumentException("An executable path cannot contain a double quote.", nameof(executablePath));

        var builder = new StringBuilder().Append('"').Append(executablePath).Append('"');
        foreach (var argument in arguments)
            builder.Append(' ').Append(Quote(argument));
        return builder.ToString();
    }

    /// <summary>
    /// The command line that runs the cmd.exe built-in <paramref name="builtin"/> through
    /// <paramref name="cmdPath"/> (see the remarks for the escaping).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The built-in is not a plain word, or an argument holds <c>%</c>, a line break or NUL.
    /// </exception>
    public static string CmdBuiltin(string cmdPath, string builtin, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(builtin);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!builtin.All(char.IsAsciiLetter))
            throw new ArgumentException($"'{builtin}' is not a cmd.exe built-in.", nameof(builtin));

        var line = new StringBuilder(builtin);
        foreach (var argument in arguments)
            line.Append(' ').Append(CmdArgument(argument));
        return Build(cmdPath, []) + " " + CmdSwitches + " \"" + line + "\"";
    }

    /// <summary>One argument of a built-in, as cmd.exe must read it (see the remarks).</summary>
    /// <exception cref="ArgumentException">The argument holds <c>%</c>, a line break or NUL.</exception>
    public static string CmdArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.IndexOfAny(['%', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException(
                "An argument of a cmd.exe built-in cannot contain '%', a line break or NUL: cmd would expand or split it.",
                nameof(argument));

        if (argument.Length > 0 && argument.All(IsPlainWordCharacter))
            return argument;
        return "\"" + argument.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsPlainWordCharacter(char c)
        => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+' or '/' or '\\' or ':' or '~' or '#' or '@'
            or '[' or ']' or '{' or '}' or '*' or '?' or '\'' or '$';
}
