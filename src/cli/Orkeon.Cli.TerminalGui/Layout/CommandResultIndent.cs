namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Hangs what a slash command writes under its echo: the first line after an elbow, the next ones aligned
/// with it. Text arrives in pieces cut anywhere, so the state is whether the next char opens a line.
/// </summary>
internal sealed class CommandResultIndent
{
    private readonly string _first;
    private readonly string _next;
    private bool _active;
    private bool _elbowDue;
    private bool _lineStart;

    public CommandResultIndent(GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        _first = $"{TranscriptModel.Indent}{glyphs.Elbow}  ";
        _next = new string(' ', TranscriptModel.Indent.Length + glyphs.Elbow.Length + 2);
    }

    /// <summary>Starts a command's result (<paramref name="active"/>), or ends the one in flight.</summary>
    public void Reset(bool active)
    {
        _active = active;
        _elbowDue = active;
        _lineStart = active;
    }

    /// <summary>What the next line opens with; nothing when no result is in flight.</summary>
    public string Next
    {
        get
        {
            if (!_active) return string.Empty;
            return _elbowDue ? _first : _next;
        }
    }

    /// <summary>The styled lines to show for <paramref name="lines"/>; the lines themselves when no result is in flight.</summary>
    public IReadOnlyList<TranscriptLine> Apply(IReadOnlyList<TranscriptLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!_active) return lines;
        var indented = new List<TranscriptLine>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Text.Length == 0)
            {
                indented.Add(line);
                continue;
            }
            var head = Next;
            _elbowDue = false;
            indented.Add(line with
            {
                Text = head + line.Text,
                Runs = [.. line.Runs.Select(r => r with { Start = r.Start + head.Length })],
                HangingIndent = line.HangingIndent + _next.Length,
                CopySource = line.CopySource ?? line.Text,
            });
        }
        _lineStart = true;
        return indented;
    }

    /// <summary>The text to show for <paramref name="text"/>; the text itself when no result is in flight.</summary>
    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!_active) return text;
        var builder = new System.Text.StringBuilder(text.Length + 8);
        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            if (c == '\n')
            {
                _lineStart = true;
            }
            else if (_lineStart)
            {
                builder.Append(_elbowDue ? _first : _next);
                _elbowDue = false;
                _lineStart = false;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }
}
