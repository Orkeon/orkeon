using System.Text;

namespace Orkeon.Tests.Shared.Launchers;

/// <summary>
/// How a Windows program built on the Microsoft C runtime — <c>orkeon.exe</c>, a .NET apphost —
/// splits the command line <c>CreateProcess</c> hands it into <c>argv</c> (STUDIO-51 § 2, reading
/// 4): a <c>"</c> opens or closes, <c>2n</c> backslashes before a <c>"</c> give <c>n</c> and a
/// delimiter, <c>2n+1</c> give <c>n</c> and a literal <c>"</c>, a backslash anywhere else is
/// literal, and <c>""</c> inside quotes is one literal <c>"</c>. A model, not the CRT: the
/// Integration suite holds it against .NET on Linux, which splits
/// <c>ProcessStartInfo.Arguments</c> by these very rules (<c>Process.Unix.cs</c>).
/// </summary>
public static class CrtArgv
{
    /// <summary>
    /// The <c>argv</c> of <paramref name="commandLine"/>: <c>argv[0]</c> by the program-name rule
    /// (quotes toggle, nothing escapes), the rest by the argument rules.
    /// </summary>
    public static IReadOnlyList<string> Split(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        var i = 0;
        var program = new StringBuilder();
        var quoted = false;
        while (i < commandLine.Length && (quoted || commandLine[i] is not (' ' or '\t')))
        {
            if (commandLine[i] == '"')
                quoted = !quoted;
            else
                program.Append(commandLine[i]);
            i++;
        }

        return [program.ToString(), .. SplitArguments(commandLine[i..])];
    }

    /// <summary>The arguments of <paramref name="arguments"/>, every one by the argument rules.</summary>
    public static IReadOnlyList<string> SplitArguments(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var results = new List<string>();
        var i = 0;
        while (true)
        {
            while (i < arguments.Length && arguments[i] is ' ' or '\t')
                i++;
            if (i == arguments.Length)
                return results;

            results.Add(NextArgument(arguments, ref i));
        }
    }

    private static string NextArgument(string arguments, ref int i)
    {
        var argument = new StringBuilder();
        var quoted = false;
        while (i < arguments.Length)
        {
            var backslashes = 0;
            while (i < arguments.Length && arguments[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (backslashes > 0)
            {
                if (i >= arguments.Length || arguments[i] != '"')
                {
                    argument.Append('\\', backslashes);
                }
                else
                {
                    argument.Append('\\', backslashes / 2);
                    if (backslashes % 2 != 0)
                    {
                        argument.Append('"');
                        i++;
                    }
                }

                continue;
            }

            var c = arguments[i];
            if (c == '"')
            {
                if (quoted && i + 1 < arguments.Length && arguments[i + 1] == '"')
                {
                    argument.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                i++;
                continue;
            }

            if (c is ' ' or '\t' && !quoted)
                break;

            argument.Append(c);
            i++;
        }

        return argument.ToString();
    }
}
