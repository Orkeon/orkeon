using System.Text;

namespace Orkeon.Analysis.Core.Editing;

internal enum InsertPosition
{
    Before,
    After,
    Inside,
}

internal sealed class NodeInsertOperation(NodeSelector anchor, InsertPosition position, string text) : IEditOperation
{
    public EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed)
    {
        var node = parsed.Resolve(anchor);
        var file = current.Span;
        var eol = IndentationDetector.LineEnding(file);
        var gap = parsed.Language == StructuralFile.MarkdownLanguage ? eol + eol : eol;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        string indent;
        var at = node.Range.EndByte;
        var lead = gap;
        var tail = string.Empty;
        var firstIndent = true;
        switch (position)
        {
            case InsertPosition.Before:
                at = node.Range.StartByte;
                indent = IndentationDetector.IndentOf(file, at);
                lead = string.Empty;
                tail = gap + indent;
                firstIndent = false;
                break;
            case InsertPosition.After:
                indent = IndentationDetector.IndentOf(file, node.Range.StartByte);
                break;
            default:
                (at, indent, lead, tail) = InsideBody(file, node, eol, gap);
                break;
        }

        var block = string.Join(eol, lines.Select((l, i) => l.Length == 0 || (i == 0 && !firstIndent) ? l : indent + l));
        var insertion = Encoding.UTF8.GetBytes(lead + block + tail);
        var blockStart = at + Encoding.UTF8.GetByteCount(lead);
        var blockLength = Encoding.UTF8.GetByteCount(block);
        var bytes = new byte[current.Length + insertion.Length];
        file[..at].CopyTo(bytes);
        insertion.CopyTo(bytes.AsSpan(at));
        file[at..].CopyTo(bytes.AsSpan(at + insertion.Length));
        var startLine = bytes.AsSpan(0, blockStart).Count((byte)'\n') + 1;
        return new EditResult
        {
            Bytes = bytes,
            Range = new NodeRange(blockStart, blockStart + blockLength, startLine, startLine + lines.Length - 1),
        };
    }

    private (int At, string Indent, string Lead, string Tail) InsideBody(ReadOnlySpan<byte> file, StructuralNode node, string eol, string gap)
    {
        if (node.Kind == "section") return (node.Range.EndByte, string.Empty, gap, string.Empty);

        var closing = node.Range.EndByte - 1;
        if (file[closing] != (byte)'}')
            throw new InvalidOperationException($"'{anchor.Text}' has no braced body to insert inside.");
        var last = closing - 1;
        while (file[last] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') last--;

        if (file[last] == (byte)'{')
        {
            var parentIndent = IndentationDetector.IndentOf(file, node.Range.StartByte);
            var sameLine = file[(last + 1)..closing].IndexOf((byte)'\n') < 0;
            return sameLine
                ? (closing, parentIndent + IndentationDetector.Unit(file), eol, eol + parentIndent)
                : (last + 1, parentIndent + IndentationDetector.Unit(file), eol, string.Empty);
        }

        var comma = node.Kind == "enum_declaration" && file[last] != (byte)',' ? "," : string.Empty;
        var first = node.Range.StartByte + file[node.Range.StartByte..].IndexOf((byte)'{') + 1;
        while (file[first] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') first++;
        return (last + 1, IndentationDetector.IndentOf(file, first), comma + eol, string.Empty);
    }
}
