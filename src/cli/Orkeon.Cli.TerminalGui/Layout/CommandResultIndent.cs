namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Hangs what a slash command writes under its echo: the first line after an elbow, the next ones aligned
/// with it. Text arrives in pieces cut anywhere, so the state is whether the next char opens a line.
/// </summary>
internal sealed class CommandResultIndent(GlyphSet glyphs)
{
    private readonly string _first = $"{TranscriptModel.Indent}{glyphs.Elbow}  ";
    private readonly string _next = new(' ', TranscriptModel.Indent.Length + glyphs.Elbow.Length + 2);
    private bool _active;
    private bool _elbowDue;
    private bool _lineStart;

    /// <summary>Starts a command's result (<paramref name="active"/>), or ends the one in flight.</summary>
    public void Reset(bool active)
    {
        _active = active;
        _elbowDue = active;
        _lineStart = active;
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
