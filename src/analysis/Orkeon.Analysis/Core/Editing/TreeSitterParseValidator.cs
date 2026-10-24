using System.Text;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Core.Editing;

/// <summary>
/// Compares the diagnostics of the file before and after a splice by position: those before the replaced span
/// match as they are, those after it shifted by the length delta, those inside the old span are gone.
/// </summary>
internal sealed class TreeSitterParseValidator(TreeSitterParserPool pool, string language) : IParseValidator
{
    public IReadOnlyList<string> Introduced(byte[] before, byte[] after, NodeRange replacement)
    {
        var delta = after.Length - before.Length;
        var known = Diagnose(before).Select(d => d.Offset).ToHashSet();
        return Diagnose(after)
            .Where(d => d.Offset < replacement.StartByte
                ? !known.Contains(d.Offset)
                : d.Offset < replacement.EndByte || !known.Contains(d.Offset - delta))
            .Select(d => d.Text)
            .ToList();
    }

    private List<(int Offset, string Text)> Diagnose(byte[] bytes)
    {
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var lineStarts = new List<int> { bom };
        for (var i = bom; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n') lineStarts.Add(i + 1);
        }

        using var parsed = pool.Parse(Encoding.UTF8.GetString(bytes.AsSpan(bom)), language);
        return parsed.Diagnostics
            .Select(d => (lineStarts[d.StartLine - 1] + d.StartColumn - 1, $"{d.StartLine}:{d.StartColumn} {d.Message}"))
            .ToList();
    }
}
