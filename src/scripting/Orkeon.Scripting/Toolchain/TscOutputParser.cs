using System.Globalization;
using System.Text.RegularExpressions;

namespace Orkeon.Scripting.Toolchain;

/// <summary>One finding of the type checker, its path already a virtual one.</summary>
/// <param name="Path">The virtual path of the file, or <see langword="null"/> for a finding that belongs to no file.</param>
/// <param name="Line">1-based line, 0 without a file.</param>
/// <param name="Column">1-based column, 0 without a file.</param>
/// <param name="Code">The <c>TSxxxx</c> code, empty when the line was not a diagnostic.</param>
/// <param name="Message">The message, physical paths redacted.</param>
public sealed record TypeCheckDiagnostic(string? Path, int Line, int Column, string Code, string Message)
{
    /// <summary><c>&lt;path&gt;:line:col TSxxxx message</c>, or <c>TSxxxx message</c> without a file.</summary>
    public override string ToString()
    {
        var head = Path is null ? string.Empty : $"{Path}:{Line}:{Column} ";
        return Code.Length == 0 ? $"{head}{Message}" : $"{head}{Code} {Message}";
    }
}

/// <summary>
/// Reads the output of <c>tsc --pretty false</c> (<c>path(line,col): error TSxxxx: message</c>, with
/// indented continuation lines) into <see cref="TypeCheckDiagnostic"/>s. Every path and every
/// message goes through the caller's redaction first: nothing physical leaves this class.
/// </summary>
public static partial class TscOutputParser
{
    [GeneratedRegex(@"^(?<path>.+?)\((?<line>\d+),(?<col>\d+)\): (?:error|warning) (?<code>TS\d+): (?<msg>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Located();

    [GeneratedRegex(@"^(?:error|warning) (?<code>TS\d+): (?<msg>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Unlocated();

    /// <summary>Parses <paramref name="output"/>.</summary>
    /// <param name="output">The compiler's stdout and stderr.</param>
    /// <param name="redactPath">Maps a physical file path to the virtual one.</param>
    /// <param name="redactText">Removes the physical paths a message carries.</param>
    public static IReadOnlyList<TypeCheckDiagnostic> Parse(
        string output, Func<string, string> redactPath, Func<string, string> redactText)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(redactPath);
        ArgumentNullException.ThrowIfNull(redactText);

        var result = new List<TypeCheckDiagnostic>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            if (char.IsWhiteSpace(line[0]) && result.Count > 0)
            {
                // A continuation of the previous message (the elaboration of a type error).
                var last = result[^1];
                result[^1] = last with { Message = last.Message + "\n" + redactText(line) };
                continue;
            }

            var m = Located().Match(line);
            if (m.Success)
            {
                result.Add(new TypeCheckDiagnostic(
                    redactPath(m.Groups["path"].Value),
                    int.Parse(m.Groups["line"].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups["col"].Value, CultureInfo.InvariantCulture),
                    m.Groups["code"].Value,
                    redactText(m.Groups["msg"].Value)));
                continue;
            }

            m = Unlocated().Match(line);
            result.Add(m.Success
                ? new TypeCheckDiagnostic(null, 0, 0, m.Groups["code"].Value, redactText(m.Groups["msg"].Value))
                : new TypeCheckDiagnostic(null, 0, 0, string.Empty, redactText(line)));
        }

        return result;
    }
}
