using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>Pure reading of one line of markdown: the text it shows and the styles over it.</summary>
internal static class MarkdownInline
{
    private static readonly char[] Markers = ['*', '_', '`', '[', '<', '&', '\\', '!'];

    /// <summary>
    /// The text <paramref name="source"/> shows. What is not a plain paragraph once read (a stray block marker)
    /// stays as it was written, so nothing typed is lost.
    /// </summary>
    public static (string Text, IReadOnlyList<TranscriptRun> Runs) Parse(
        string source, bool bold = false, bool italic = false, PaletteRole role = PaletteRole.Normal)
    {
        ArgumentNullException.ThrowIfNull(source);
        var text = new StringBuilder(source.Length);
        var runs = new List<TranscriptRun>();
        ContainerInline? inlines = null;
        if (source.AsSpan().IndexOfAny(Markers) >= 0)
        {
            var document = Markdig.Markdown.Parse(source);
            if (document is [ParagraphBlock paragraph]) inlines = paragraph.Inline;
        }

        if (inlines is null) Add(text, runs, source, new Style(bold, italic, role));
        else Collect(inlines, text, runs, new Style(bold, italic, role));
        return (text.ToString(), runs);
    }

    private readonly record struct Style(bool Bold, bool Italic, PaletteRole Role);

    private static void Collect(ContainerInline container, StringBuilder text, List<TranscriptRun> runs, Style style)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    Add(text, runs, literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    Add(text, runs, code.Content, style with { Role = PaletteRole.Code });
                    break;
                case EmphasisInline emphasis:
                    Collect(emphasis, text, runs, emphasis.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true });
                    break;
                case LinkInline link:
                    Link(link, text, runs, style);
                    break;
                case AutolinkInline autolink:
                    Add(text, runs, autolink.Url, style with { Role = PaletteRole.FileRef });
                    break;
                case HtmlInline html:
                    Add(text, runs, html.Tag, style);
                    break;
                case HtmlEntityInline entity:
                    Add(text, runs, entity.Transcoded.ToString(), style);
                    break;
                case LineBreakInline:
                    Add(text, runs, " ", style);
                    break;
                case ContainerInline other:
                    Collect(other, text, runs, style);
                    break;
            }
        }
    }

    private static void Link(LinkInline link, StringBuilder text, List<TranscriptRun> runs, Style style)
    {
        var start = text.Length;
        Collect(link, text, runs, style with { Role = PaletteRole.FileRef });
        var label = text.ToString(start, text.Length - start);
        if (!string.IsNullOrEmpty(link.Url) && label != link.Url)
            Add(text, runs, $" ({link.Url})", style with { Role = PaletteRole.Dim });
    }

    private static void Add(StringBuilder text, List<TranscriptRun> runs, string piece, Style style)
    {
        if (piece.Length == 0) return;
        if (style.Bold || style.Italic || style.Role != PaletteRole.Normal)
            runs.Add(new TranscriptRun(text.Length, piece.Length, style.Role, style.Bold, style.Italic));
        text.Append(piece);
    }
}
