using System.Text;

namespace Orkeon.Host.Gateway;

/// <summary>
/// The Markdown of an answer as Teams may render it. Teams also renders HTML, and loads the images of either: a text a
/// model was made to write would reach out to an address of its choice as soon as someone looks at it. Outside code,
/// HTML shows as it was written and an image is a link.
/// </summary>
internal static class TeamsMarkdown
{
    /// <summary><paramref name="text"/> with neither HTML nor images outside code.</summary>
    public static string Harmless(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length + 64);
        (char Mark, int Length)? fence = null;
        for (var start = 0; start < text.Length;)
        {
            var feed = text.IndexOf('\n', start);
            var end = feed < 0 ? text.Length : feed + 1;
            var line = text.AsSpan(start, end - start);
            start = end;

            var mark = FenceOf(line, out var info);
            if (fence is { } open)
            {
                result.Append(line);
                if (mark is { } close && close.Mark == open.Mark && close.Length >= open.Length && info.IsWhiteSpace())
                    fence = null;
            }
            // The info string of a fence of backticks holds none, or it is a code span.
            else if (mark is { } opened && (opened.Mark == '~' || !info.Contains('`')))
            {
                fence = opened;
                AppendProse(result, line);
            }
            else
            {
                AppendLine(result, line);
            }
        }

        return result.ToString();
    }

    private static (char Mark, int Length)? FenceOf(ReadOnlySpan<char> line, out ReadOnlySpan<char> info)
    {
        info = default;
        var rest = line.TrimStart(' ');
        if (line.Length - rest.Length > 3 || rest.Length == 0 || rest[0] is not ('`' or '~'))
            return null;
        var length = RunLength(rest, 0);
        info = rest[length..];
        return length >= 3 ? (rest[0], length) : null;
    }

    /// <summary>A line outside a fence: its code spans as they are. A span open at its end is not one here.</summary>
    private static void AppendLine(StringBuilder result, ReadOnlySpan<char> line)
    {
        var prose = 0;
        for (var i = 0; i < line.Length;)
        {
            if (line[i] != '`')
            {
                i++;
                continue;
            }

            var length = RunLength(line, i);
            var escaped = i > 0 && line[i - 1] == '\\';
            var close = escaped ? -1 : CloseOf(line, i + length, length);
            if (close < 0)
            {
                i += length;
                continue;
            }

            AppendProse(result, line[prose..i]);
            result.Append(line[i..(close + length)]);
            i = prose = close + length;
        }

        AppendProse(result, line[prose..]);
    }

    private static int CloseOf(ReadOnlySpan<char> line, int from, int length)
    {
        for (var i = from; i < line.Length;)
        {
            var run = line[i] == '`' ? RunLength(line, i) : 0;
            if (run == length)
                return i;
            i += Math.Max(run, 1);
        }

        return -1;
    }

    private static int RunLength(ReadOnlySpan<char> text, int from)
    {
        var end = from;
        while (end < text.Length && text[end] == text[from])
            end++;
        return end - from;
    }

    private static void AppendProse(StringBuilder result, ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<')
            {
                result.Append("&lt;");
                continue;
            }

            if (text[i] == '!')
            {
                // All of them: the one left would open an image again.
                var run = RunLength(text, i);
                if (i + run < text.Length && text[i + run] == '[')
                {
                    i += run - 1;
                    continue;
                }

                result.Append(text.Slice(i, run));
                i += run - 1;
                continue;
            }

            result.Append(text[i]);
        }
    }
}
