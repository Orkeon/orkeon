using System.Text;

namespace Orkeon.Analysis.Core.Editing;

internal sealed class NodeReplaceOperation(NodeSelector selector, string expectedHash, string text) : IEditOperation
{
    public EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed)
    {
        var node = parsed.Resolve(selector);
        if (!string.Equals(node.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new StaleHashException(expectedHash, node.Sha256);
        var replacement = Encoding.UTF8.GetBytes(WithLineEndingsOf(current.Span, text));
        var bytes = new byte[current.Length - (node.Range.EndByte - node.Range.StartByte) + replacement.Length];
        current.Span[..node.Range.StartByte].CopyTo(bytes);
        replacement.CopyTo(bytes.AsSpan(node.Range.StartByte));
        current.Span[node.Range.EndByte..].CopyTo(bytes.AsSpan(node.Range.StartByte + replacement.Length));
        return new EditResult
        {
            Bytes = bytes,
            Range = node.Range with
            {
                EndByte = node.Range.StartByte + replacement.Length,
                EndLine = node.Range.StartLine + replacement.Count((byte)'\n'),
            },
        };
    }

    private static string WithLineEndingsOf(ReadOnlySpan<byte> file, string value)
    {
        var normalised = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        return IndentationDetector.LineEnding(file) == "\r\n" ? normalised.Replace("\n", "\r\n", StringComparison.Ordinal) : normalised;
    }
}
