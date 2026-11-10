using System.Text;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Markdown arriving in pieces cut anywhere: the finished lines come out styled, the line being written comes out as
/// it stands, to be replaced once it is finished.
/// </summary>
internal sealed class MarkdownStream
{
    private readonly MarkdownLines _lines;
    private readonly StringBuilder _partial;

    public MarkdownStream(GlyphSet glyphs)
    {
        _lines = new MarkdownLines(glyphs);
        _partial = new StringBuilder();
    }

    /// <summary>Whether what is shown of this stream is under a command's echo.</summary>
    public bool UnderCommand { get; set; }

    /// <summary>Whether nothing is waiting for its line to end.</summary>
    public bool IsEmpty => _partial.Length == 0 && !_lines.Holding;

    /// <summary>The lines <paramref name="text"/> finishes, and the unfinished line to show under them.</summary>
    public (IReadOnlyList<TranscriptLine> Lines, string Partial) Write(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        var finished = new List<TranscriptLine>();
        foreach (var c in text)
        {
            if (c == '\n')
            {
                finished.AddRange(_lines.Feed(Taken(), width));
            }
            else
            {
                _partial.Append(c);
            }
        }

        // A table's rows would only flash: they are redrawn together when it ends.
        var partial = _partial.ToString().TrimEnd('\r');
        var hidden = _lines.Holding || partial.TrimStart().StartsWith('|');
        return (finished, hidden ? string.Empty : partial);
    }

    /// <summary>Everything still waiting, as it stands.</summary>
    public IReadOnlyList<TranscriptLine> Flush(int width)
    {
        var finished = new List<TranscriptLine>();
        if (_partial.Length > 0) finished.AddRange(_lines.Feed(Taken(), width));
        finished.AddRange(_lines.Flush(width));
        return finished;
    }

    private string Taken()
    {
        var line = _partial.ToString().TrimEnd('\r');
        _partial.Clear();
        return line;
    }
}
