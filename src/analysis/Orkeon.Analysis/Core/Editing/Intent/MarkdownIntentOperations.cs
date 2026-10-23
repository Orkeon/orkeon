using System.Text;
using Markdig;
using Markdig.Syntax;

namespace Orkeon.Analysis.Core.Editing.Intent;

/// <summary>Creates a Markdown heading path once, each segment at the level of its depth.</summary>
internal sealed class EnsureSectionOperation(string headingPath, int? level = null) : IEditOperation
{
    public EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed)
    {
        var file = current.Span;
        var segments = headingPath.Split('/');
        var found = 0;
        var at = EndOfContent(file);
        StructuralNode? existing = null;
        var baseLevel = 0;
        while (found < segments.Length && TryResolve(parsed, string.Join('/', segments[..(found + 1)])) is { } section)
        {
            existing = section;
            baseLevel = HeadingLevel(section);
            at = section.Range.EndByte;
            found++;
        }

        if (found == segments.Length)
            return new EditResult { Bytes = current.ToArray(), Range = existing!.Range, Applied = false };

        var eol = IndentationDetector.LineEnding(file);
        var headings = Enumerable.Range(found, segments.Length - found).Select(i =>
            new string('#', i == segments.Length - 1 ? level ?? baseLevel + i - found + 1 : baseLevel + i - found + 1) + " " + segments[i]);
        var lead = at == 0 ? string.Empty : eol + eol;
        return MarkdownText.Insert(current, at, lead, string.Join(eol + eol, headings));
    }

    private static int HeadingLevel(StructuralNode section) =>
        Markdown.Parse(section.Text).Descendants<HeadingBlock>().First().Level;

    private static StructuralNode? TryResolve(StructuralFile parsed, string path)
    {
        try
        {
            return parsed.Resolve(NodeSelector.Parse(path));
        }
        catch (SelectorNotFoundException)
        {
            return null;
        }
    }

    private static int EndOfContent(ReadOnlySpan<byte> file)
    {
        var end = file.Length;
        while (end > 0 && file[end - 1] is (byte)'\r' or (byte)'\n') end--;
        return end;
    }
}

/// <summary>Inserts text at the end of a heading's section, before the next heading of the same or higher level.</summary>
internal sealed class InsertUnderHeadingOperation(NodeSelector heading, string text) : IEditOperation
{
    public EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed)
    {
        var eol = IndentationDetector.LineEnding(current.Span);
        var body = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", eol, StringComparison.Ordinal);
        return MarkdownText.Insert(current, parsed.Resolve(heading).Range.EndByte, eol + eol, body);
    }
}

/// <summary>Replaces the body of the index-th mermaid fence of a section.</summary>
internal sealed class ReplaceMermaidOperation(NodeSelector section, int index, string text) : IEditOperation
{
    public EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed)
    {
        var scope = parsed.Resolve(section).Range;
        var file = current.Span;
        var fence = parsed.CodeFences()
            .Where(f => f.Range.StartByte >= scope.StartByte && f.Range.EndByte <= scope.EndByte && IsMermaid(OpeningLine(current.Span, f.Range.StartByte)))
            .ElementAtOrDefault(index)
            ?? throw new SelectorNotFoundException(parsed.Outline().Select(n => n.Selector).ToList());
        var start = fence.Range.StartByte;
        var rest = file[start..];
        var firstNewline = rest.IndexOf((byte)'\n');
        var closeAt = ClosingFence(rest, firstNewline);
        var closed = closeAt >= 0;
        var eol = IndentationDetector.LineEnding(file);
        var content = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", eol, StringComparison.Ordinal);
        int from, to;
        var lead = string.Empty;
        string tail;
        if (closed)
        {
            from = start + firstNewline + 1;
            to = start + closeAt;
            tail = eol;
        }
        else
        {
            from = firstNewline >= 0 ? start + firstNewline + 1 : file.Length;
            to = file.Length;
            if (firstNewline < 0) lead = eol;
            tail = to > from && file[to - 1] == (byte)'\n' ? eol : string.Empty;
        }

        var body = lead + content + tail;
        var insertion = Encoding.UTF8.GetBytes(body);
        var range = new NodeRange(from, from + insertion.Length, fence.Range.StartLine + 1, fence.Range.StartLine + content.Count(c => c == '\n') + 1);
        if (file[from..to].SequenceEqual(insertion))
            return new EditResult { Bytes = current.ToArray(), Range = range, Applied = false };
        var bytes = new byte[file.Length - (to - from) + insertion.Length];
        file[..from].CopyTo(bytes);
        insertion.CopyTo(bytes.AsSpan(from));
        file[to..].CopyTo(bytes.AsSpan(from + insertion.Length));
        return new EditResult { Bytes = bytes, Range = range };
    }

    private static string OpeningLine(ReadOnlySpan<byte> file, int start)
    {
        var line = file[start..];
        var end = line.IndexOf((byte)'\n');
        return Encoding.UTF8.GetString(end >= 0 ? line[..end] : line);
    }

    private static bool IsMermaid(string opening) => opening.AsSpan().Trim().TrimStart("`~").SequenceEqual("mermaid");

    private static int ClosingFence(ReadOnlySpan<byte> rest, int firstNewline)
    {
        var marker = rest[0];
        var length = rest.IndexOfAnyExcept(marker) is var run and >= 0 ? run : rest.Length;
        var position = firstNewline + 1;
        while (firstNewline >= 0 && position < rest.Length)
        {
            var line = rest[position..];
            var end = line.IndexOf((byte)'\n');
            var trimmed = (end >= 0 ? line[..end] : line).TrimStart((byte)' ');
            if ((trimmed.IndexOfAnyExcept(marker) is var count and >= 0 ? count : trimmed.Length) >= length) return position;
            if (end < 0) break;
            position += end + 1;
        }

        return -1;
    }
}

internal static class MarkdownText
{
    internal static EditResult Insert(ReadOnlyMemory<byte> current, int at, string lead, string block)
    {
        var insertion = Encoding.UTF8.GetBytes(lead + block);
        var file = current.Span;
        var bytes = new byte[file.Length + insertion.Length];
        file[..at].CopyTo(bytes);
        insertion.CopyTo(bytes.AsSpan(at));
        file[at..].CopyTo(bytes.AsSpan(at + insertion.Length));
        var blockStart = at + Encoding.UTF8.GetByteCount(lead);
        var startLine = bytes.AsSpan(0, blockStart).Count((byte)'\n') + 1;
        return new EditResult
        {
            Bytes = bytes,
            Range = new NodeRange(
                blockStart, blockStart + Encoding.UTF8.GetByteCount(block), startLine, startLine + block.Count(c => c == '\n')),
        };
    }
}
