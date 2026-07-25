using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Chunking;

/// <summary>
/// Splits text using a hierarchy of separators, recursively trying finer-grained
/// separators when chunks are still too large, and merging small adjacent parts
/// up to <see cref="ChunkingOptions.MaxChunkSize"/> with
/// <see cref="ChunkingOptions.Overlap"/> characters of continuity between chunks.
/// Default separator hierarchy: paragraph breaks, line breaks, sentences, words, characters.
/// </summary>
/// <remarks>
/// Canonical port of the historical <c>Orkeon.Infrastructure.Knowledge.Chunking.RecursiveTextChunker</c>
/// (RAG-02/C3 consolidation), extended with exact global offsets.
/// </remarks>
public sealed class RecursiveChunkingStrategy : ChunkingStrategyBase
{
    internal static readonly string[] DefaultSeparators = ["\n\n", "\n", ". ", " ", ""];

    private readonly IReadOnlyList<string> _separators;

    /// <summary>
    /// Initializes the strategy with the default separator hierarchy, or a custom one.
    /// </summary>
    /// <param name="separators">
    /// Optional custom separators, coarsest first. An empty-string separator (or exhausting
    /// the list) force-splits by character count.
    /// </param>
    public RecursiveChunkingStrategy(IReadOnlyList<string>? separators = null)
    {
        _separators = separators is { Count: > 0 } ? separators : DefaultSeparators;
    }

    /// <inheritdoc />
    public override string Name => "recursive";

    internal override List<ChunkSlice> ComputeSlices(
        string content, int maxChunkSize, int overlap, ChunkingOptions options)
    {
        var slices = new List<ChunkSlice>();
        SplitSpan(content, 0, _separators, 0, maxChunkSize, overlap, slices);
        return slices;
    }

    /// <summary>
    /// Recursively splits <paramref name="text"/> (a contiguous slice of the original
    /// document starting at <paramref name="globalOffset"/>) into slices of at most
    /// <paramref name="chunkSize"/> characters. Shared with <see cref="StructuralChunkingStrategy"/>
    /// for sub-splitting oversized sections.
    /// </summary>
    internal static void SplitSpan(
        string text,
        int globalOffset,
        IReadOnlyList<string> separators,
        int separatorIndex,
        int chunkSize,
        int overlap,
        List<ChunkSlice> result)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // If text fits in a single chunk, emit it.
        if (text.Length <= chunkSize)
        {
            result.Add(new ChunkSlice(globalOffset, globalOffset + text.Length));
            return;
        }

        // If no more separators, force-split by character count.
        if (separatorIndex >= separators.Count || string.IsNullOrEmpty(separators[separatorIndex]))
        {
            ForceSplit(text, globalOffset, chunkSize, overlap, result);
            return;
        }

        var separator = separators[separatorIndex];
        var parts = SplitKeepingPositions(text, separator);

        if (parts.Count <= 1)
        {
            // This separator didn't split anything; try the next separator.
            SplitSpan(text, globalOffset, separators, separatorIndex + 1, chunkSize, overlap, result);
            return;
        }

        // Merge small parts into chunks that fit within chunkSize.
        var mergedChunks = MergeParts(parts, globalOffset, chunkSize, overlap);

        foreach (var (mergedText, mergedOffset) in mergedChunks)
        {
            if (mergedText.Length <= chunkSize)
            {
                result.Add(new ChunkSlice(mergedOffset, mergedOffset + mergedText.Length));
            }
            else
            {
                // Still too large, recurse with the next separator.
                SplitSpan(mergedText, mergedOffset, separators, separatorIndex + 1, chunkSize, overlap, result);
            }
        }
    }

    private static void ForceSplit(
        string text,
        int globalOffset,
        int chunkSize,
        int overlap,
        List<ChunkSlice> result)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            var end = Math.Min(pos + chunkSize, text.Length);
            result.Add(new ChunkSlice(globalOffset + pos, globalOffset + end));

            var advance = chunkSize - overlap;
            if (advance <= 0)
                advance = 1; // prevent infinite loop
            pos += advance;

            // Avoid creating a tiny trailing chunk that's entirely overlap.
            if (pos < text.Length && text.Length - pos <= overlap)
            {
                result.Add(new ChunkSlice(globalOffset + pos, globalOffset + text.Length));
                break;
            }
        }
    }

    /// <summary>
    /// Splits text by separator and returns (part, offset) pairs; the separator stays
    /// attached to the preceding part, so parts tile the input contiguously.
    /// </summary>
    private static List<(string Text, int Offset)> SplitKeepingPositions(string text, string separator)
    {
        var parts = new List<(string Text, int Offset)>();
        int startPos = 0;

        while (startPos < text.Length)
        {
            var idx = text.IndexOf(separator, startPos, StringComparison.Ordinal);
            if (idx < 0)
            {
                parts.Add((text[startPos..], startPos));
                break;
            }

            var endPos = idx + separator.Length;
            parts.Add((text[startPos..endPos], startPos));
            startPos = endPos;
        }

        return parts;
    }

    /// <summary>
    /// Merges adjacent small parts until they approach chunkSize, respecting overlap.
    /// Merged texts remain contiguous slices of the original document.
    /// </summary>
    private static List<(string Text, int Offset)> MergeParts(
        List<(string Text, int Offset)> parts,
        int globalOffset,
        int chunkSize,
        int overlap)
    {
        var merged = new List<(string Text, int Offset)>();
        var currentText = new System.Text.StringBuilder();
        int currentOffset = -1;

        foreach (var (partText, partOffset) in parts)
        {
            var adjustedOffset = globalOffset + partOffset;

            if (currentText.Length == 0)
            {
                currentText.Append(partText);
                currentOffset = adjustedOffset;
            }
            else if (currentText.Length + partText.Length <= chunkSize)
            {
                currentText.Append(partText);
            }
            else
            {
                merged.Add((currentText.ToString(), currentOffset));

                // Start a new chunk, potentially carrying overlap from the previous one.
                if (overlap > 0)
                {
                    var prevText = currentText.ToString();
                    var overlapStart = Math.Max(0, prevText.Length - overlap);
                    var overlapText = prevText[overlapStart..];
                    currentText.Clear();
                    currentText.Append(overlapText);
                    currentOffset = adjustedOffset - overlapText.Length;
                    currentText.Append(partText);
                }
                else
                {
                    currentText.Clear();
                    currentText.Append(partText);
                    currentOffset = adjustedOffset;
                }
            }
        }

        if (currentText.Length > 0)
        {
            merged.Add((currentText.ToString(), currentOffset));
        }

        return merged;
    }
}
