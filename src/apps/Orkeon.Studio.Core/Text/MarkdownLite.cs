using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Orkeon.Studio.Core.Text;

/// <summary>What a block of a message is.</summary>
public enum MarkdownBlockKind
{
    /// <summary>Running text; its runs may hold <c>\n</c> runs for the line breaks the author typed.</summary>
    Paragraph,

    /// <summary>A <c># heading</c>; <see cref="MarkdownBlock.Level"/> is 1 to 6.</summary>
    Heading,

    /// <summary>A <c>- item</c>; <see cref="MarkdownBlock.Indent"/> is its nesting depth.</summary>
    Bullet,

    /// <summary>A <c>1. item</c>; <see cref="MarkdownBlock.Number"/> is the number typed.</summary>
    Numbered,

    /// <summary>A fenced block; one run holding the raw lines.</summary>
    Code,
}

/// <summary>One stretch of text with one formatting; a run of <c>\n</c> alone is a line break.</summary>
/// <param name="Text">The text, markers removed.</param>
/// <param name="Bold"><c>**bold**</c> or <c>__bold__</c>.</param>
/// <param name="Italic"><c>*italic*</c> or <c>_italic_</c>.</param>
/// <param name="Code">Inline <c>`code`</c>.</param>
public sealed record MarkdownRun(string Text, bool Bold = false, bool Italic = false, bool Code = false)
{
    /// <summary>Whether this run is a line break the author typed.</summary>
    public bool IsLineBreak => Text == "\n";
}

/// <summary>One block of a message, as the chat renders it.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Runs">Its text, formatting by formatting.</param>
/// <param name="Level">A heading's level.</param>
/// <param name="Number">A numbered item's number.</param>
/// <param name="Indent">A list item's nesting depth, 0 at the margin.</param>
public sealed record MarkdownBlock(
    MarkdownBlockKind Kind, IReadOnlyList<MarkdownRun> Runs, int Level = 0, int Number = 0, int Indent = 0)
{
    /// <summary>The block's text with the formatting dropped.</summary>
    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// The little Markdown an assistant's bubble needs (STUDIO-57): bold, italic, inline code,
/// bullet and numbered lists, headings and fenced code, read line by line. Anything else is
/// text as typed — a message that is no Markdown at all is one paragraph of plain runs, so a
/// caller renders every message through here and never has to guess. No HTML, no tables, no
/// images, no links beyond their visible text: a chat bubble shows none of those.
/// </summary>
public static class MarkdownLite
{
    /// <summary>Reads <paramref name="text"/> into blocks; null or blank is no block.</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string? text)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrWhiteSpace(text))
            return blocks;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var paragraph = new List<MarkdownRun>();
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(ReadFence(lines, ref i));
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph(blocks, paragraph);
            }
            else if (TryBlock(line, trimmed, out var block))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(block);
            }
            else
            {
                if (paragraph.Count > 0)
                    paragraph.Add(new MarkdownRun("\n"));
                paragraph.AddRange(ParseInlines(trimmed.TrimEnd()));
            }

            i++;
        }

        FlushParagraph(blocks, paragraph);
        return blocks;
    }

    /// <summary>
    /// The fenced code block opening at <paramref name="i"/>, read verbatim up to the closing
    /// fence (or the end); <paramref name="i"/> is left on the line after it.
    /// </summary>
    private static MarkdownBlock ReadFence(string[] lines, ref int i)
    {
        var code = new StringBuilder();
        i++;
        while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            if (code.Length > 0)
                code.Append('\n');
            code.Append(lines[i]);
            i++;
        }

        i++; // the closing fence, when there is one
        return new MarkdownBlock(MarkdownBlockKind.Code, [new MarkdownRun(code.ToString(), Code: true)]);
    }

    /// <summary>A heading, bullet or numbered item read from one line; false for paragraph text.</summary>
    private static bool TryBlock(string line, string trimmed, [NotNullWhen(true)] out MarkdownBlock? block)
    {
        if (TryHeading(trimmed, out var level, out var headingText))
        {
            block = new MarkdownBlock(MarkdownBlockKind.Heading, ParseInlines(headingText), Level: level);
            return true;
        }

        var indent = (line.Length - trimmed.Length) / 2;
        if (TryBullet(trimmed, out var bulletText))
        {
            block = new MarkdownBlock(MarkdownBlockKind.Bullet, ParseInlines(bulletText), Indent: indent);
            return true;
        }

        if (TryNumbered(trimmed, out var number, out var numberedText))
        {
            block = new MarkdownBlock(MarkdownBlockKind.Numbered, ParseInlines(numberedText), Number: number, Indent: indent);
            return true;
        }

        block = null;
        return false;
    }

    /// <summary>Whether <paramref name="text"/> carries any Markdown worth rendering.</summary>
    public static bool LooksLikeMarkdown(string? text) =>
        Parse(text).Any(block => block.Kind != MarkdownBlockKind.Paragraph || block.Runs.Any(run => run.Bold || run.Italic || run.Code));

    /// <summary>
    /// <paramref name="text"/> with the Markdown markers dropped — what a copy of the bubble
    /// should carry when the destination takes plain text: a bullet keeps its dash, a heading
    /// its line, bold and italic their words only.
    /// </summary>
    public static string ToPlainText(string? text)
    {
        var builder = new StringBuilder();
        foreach (var block in Parse(text))
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(block.Kind switch
            {
                MarkdownBlockKind.Bullet => new string(' ', block.Indent * 2) + "- " + block.PlainText,
                MarkdownBlockKind.Numbered => new string(' ', block.Indent * 2) + block.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + block.PlainText,
                _ => block.PlainText,
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// The inline formatting of one line: <c>**bold**</c>, <c>__bold__</c>, <c>*italic*</c>,
    /// <c>_italic_</c> at word boundaries, <c>`code`</c> (verbatim inside), <c>[text](url)</c>
    /// as its text, and a backslash before a marker keeps the marker as a character.
    /// </summary>
    public static IReadOnlyList<MarkdownRun> ParseInlines(string? text)
    {
        var runs = new List<MarkdownRun>();
        if (string.IsNullOrEmpty(text))
            return runs;

        var buffer = new StringBuilder();
        var bold = false;
        var italic = false;
        var i = 0;
        while (i < text.Length)
        {
            if (TryEscape(text, i, buffer, out var next)
                || TryCodeSpan(text, i, runs, buffer, bold, italic, out next)
                || TryLinkText(text, i, buffer, out next)
                || TryEmphasis(text, i, runs, buffer, ref bold, ref italic, out next))
            {
                i = next;
                continue;
            }

            buffer.Append(text[i]);
            i++;
        }

        Flush(runs, buffer, bold, italic);
        return runs;
    }

    /// <summary>A backslash before a marker keeps the marker as a character.</summary>
    private static bool TryEscape(string text, int at, StringBuilder buffer, out int next)
    {
        next = at;
        if (text[at] != '\\' || at + 1 >= text.Length || !"*_`[\\".Contains(text[at + 1], StringComparison.Ordinal))
            return false;

        buffer.Append(text[at + 1]);
        next = at + 2;
        return true;
    }

    /// <summary>A <c>`code`</c> span, verbatim inside; a lone backtick is a character.</summary>
    private static bool TryCodeSpan(string text, int at, List<MarkdownRun> runs, StringBuilder buffer, bool bold, bool italic, out int next)
    {
        next = at;
        if (text[at] != '`')
            return false;

        var close = text.IndexOf('`', at + 1);
        if (close <= at + 1)
            return false;

        Flush(runs, buffer, bold, italic);
        runs.Add(new MarkdownRun(text[(at + 1)..close], Code: true));
        next = close + 1;
        return true;
    }

    /// <summary>A <c>[text](url)</c> link, kept as its text.</summary>
    private static bool TryLinkText(string text, int at, StringBuilder buffer, out int next)
    {
        next = at;
        if (text[at] != '[' || !TryLink(text, at, out var linkText, out var end))
            return false;

        buffer.Append(linkText);
        next = end;
        return true;
    }

    /// <summary>A bold (<c>**</c>, <c>__</c>) or italic (<c>*</c>, <c>_</c>) delimiter that opens or closes a span.</summary>
    private static bool TryEmphasis(string text, int at, List<MarkdownRun> runs, StringBuilder buffer, ref bool bold, ref bool italic, out int next)
    {
        next = at;
        var c = text[at];
        if (c != '*' && c != '_')
            return false;

        if (at + 1 < text.Length && text[at + 1] == c && DelimiterCloses(text, at, 2, c, bold))
        {
            Flush(runs, buffer, bold, italic);
            bold = !bold;
            next = at + 2;
            return true;
        }

        if (DelimiterCloses(text, at, 1, c, italic))
        {
            Flush(runs, buffer, bold, italic);
            italic = !italic;
            next = at + 1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a delimiter of <paramref name="width"/> at <paramref name="at"/> opens a span
    /// that is closed later on the line (when none is open), or closes the open one. An
    /// opener is followed by a non-space; an underscore opener also sits at a word start,
    /// so <c>snake_case_names</c> stay as typed.
    /// </summary>
    private static bool DelimiterCloses(string text, int at, int width, char marker, bool open)
    {
        var after = at + width;
        if (open)
            return at == 0 || !char.IsWhiteSpace(text[at - 1]);

        if (after >= text.Length || char.IsWhiteSpace(text[after]))
            return false;

        if (marker == '_' && at > 0 && char.IsLetterOrDigit(text[at - 1]))
            return false;

        var closer = new string(marker, width);
        var close = text.IndexOf(closer, after, StringComparison.Ordinal);
        while (close > 0)
        {
            if (!char.IsWhiteSpace(text[close - 1]))
            {
                // A double marker is never read as two singles around nothing.
                if (width == 1 && close + 1 < text.Length && text[close + 1] == marker && text[close - 1] != marker)
                {
                    close = text.IndexOf(closer, close + 2, StringComparison.Ordinal);
                    continue;
                }

                return true;
            }

            close = text.IndexOf(closer, close + width, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryLink(string text, int at, out string linkText, out int end)
    {
        linkText = "";
        end = at;
        var closeBracket = text.IndexOf(']', at + 1);
        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
            return false;

        var closeParen = text.IndexOf(')', closeBracket + 2);
        if (closeParen < 0)
            return false;

        linkText = text[(at + 1)..closeBracket];
        end = closeParen + 1;
        return linkText.Length > 0;
    }

    private static bool TryHeading(string trimmed, out int level, out string text)
    {
        level = 0;
        text = "";
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        if (level is 0 or > 6 || level >= trimmed.Length || trimmed[level] != ' ')
        {
            level = 0;
            return false;
        }

        text = trimmed[(level + 1)..].Trim();
        return text.Length > 0;
    }

    private static bool TryBullet(string trimmed, out string text)
    {
        text = "";
        if (trimmed.Length < 3 || trimmed[0] is not ('-' or '*' or '+') || trimmed[1] != ' ')
            return false;

        text = trimmed[2..].TrimStart();
        return text.Length > 0;
    }

    private static bool TryNumbered(string trimmed, out int number, out string text)
    {
        number = 0;
        text = "";
        var digits = 0;
        while (digits < trimmed.Length && digits < 3 && char.IsAsciiDigit(trimmed[digits]))
            digits++;

        if (digits == 0 || digits + 1 >= trimmed.Length || trimmed[digits] is not ('.' or ')') || trimmed[digits + 1] != ' ')
            return false;

        number = int.Parse(trimmed[..digits], System.Globalization.CultureInfo.InvariantCulture);
        text = trimmed[(digits + 2)..].TrimStart();
        return text.Length > 0;
    }

    private static void Flush(List<MarkdownRun> runs, StringBuilder buffer, bool bold, bool italic)
    {
        if (buffer.Length == 0)
            return;

        runs.Add(new MarkdownRun(buffer.ToString(), Bold: bold, Italic: italic));
        buffer.Clear();
    }

    private static void FlushParagraph(List<MarkdownBlock> blocks, List<MarkdownRun> paragraph)
    {
        if (paragraph.Count == 0)
            return;

        blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, [.. paragraph]));
        paragraph.Clear();
    }
}
