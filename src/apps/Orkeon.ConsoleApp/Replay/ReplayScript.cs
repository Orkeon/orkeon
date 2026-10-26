namespace Orkeon.ConsoleApp.Replay;

/// <summary>One line of a replay file the REPL reads as if typed, with its number in the file (from 1).</summary>
/// <param name="Number">The line's number in the file, blank and comment lines counted.</param>
/// <param name="Text">The line as typed: a command, free text, or the answer to a question.</param>
internal sealed record ReplayLine(int Number, string Text);

/// <summary>
/// The lines of a replay file (<c>orkeon-repl --replay</c>, <c>/replay</c>). Lines are separated by LF or
/// CRLF, a trailing <c>\r</c> is stripped; a blank line, or one whose first non-space character is
/// <c>#</c>, is skipped; every other line is kept as it is, to be fed exactly as if typed.
/// </summary>
internal static class ReplayScript
{
    /// <summary>Parses the content of a replay file.</summary>
    /// <param name="content">The whole file.</param>
    /// <returns>The lines to replay, in order, with their numbers in the file.</returns>
    public static IReadOnlyList<ReplayLine> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var lines = new List<ReplayLine>();
        var number = 0;
        foreach (var raw in content.Split('\n'))
        {
            number++;
            var text = raw.EndsWith('\r') ? raw[..^1] : raw;
            var trimmed = text.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;
            lines.Add(new ReplayLine(number, text));
        }

        return lines;
    }
}
