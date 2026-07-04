using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Knowledge.Chunking;

/// <summary>
/// Splits text using a hierarchy of separators, recursively trying finer-grained
/// separators when chunks are still too large.
/// Default separator hierarchy: paragraph breaks, line breaks, sentences, words, characters.
/// </summary>
public sealed class RecursiveTextChunker : ITextChunker
{
    private static readonly string[] DefaultSeparators = ["\n\n", "\n", ". ", " ", ""];

    /// <inheritdoc />
    public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<TextChunk>();

        options ??= new ChunkingOptions();
        var separators = options.Separators ?? DefaultSeparators;
        var chunkSize = Math.Max(1, options.ChunkSize);
        var overlap = Math.Max(0, Math.Min(options.ChunkOverlap, chunkSize - 1));

        var chunks = new List<TextChunk>();
        SplitRecursive(text, 0, separators, 0, chunkSize, overlap, chunks);
        return chunks.AsReadOnly();
    }

    private static void SplitRecursive(
        string text,
        int globalOffset,
        IReadOnlyList<string> separators,
        int separatorIndex,
        int chunkSize,
        int overlap,
        List<TextChunk> result)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // If text fits in a single chunk, emit it
        if (text.Length <= chunkSize)
        {
            result.Add(new TextChunk(
                Content: text,
                StartIndex: globalOffset,
                EndIndex: globalOffset + text.Length));
            return;
        }

        // If no more separators, force-split by character count
        if (separatorIndex >= separators.Count || string.IsNullOrEmpty(separators[separatorIndex]))
        {
            ForceSplit(text, globalOffset, chunkSize, overlap, result);
            return;
        }

        var separator = separators[separatorIndex];
        var parts = SplitKeepingPositions(text, separator);

        if (parts.Count <= 1)
        {
            // This separator didn't split anything; try next separator
            SplitRecursive(text, globalOffset, separators, separatorIndex + 1, chunkSize, overlap, result);
            return;
        }

        // Merge small parts into chunks that fit within chunkSize
        var mergedChunks = MergeParts(parts, globalOffset, chunkSize, overlap);

        foreach (var (mergedText, mergedOffset) in mergedChunks)
        {
            if (mergedText.Length <= chunkSize)
            {
                result.Add(new TextChunk(
                    Content: mergedText,
                    StartIndex: mergedOffset,
                    EndIndex: mergedOffset + mergedText.Length));
            }
            else
            {
                // Still too large, recurse with next separator
                SplitRecursive(mergedText, mergedOffset, separators, separatorIndex + 1, chunkSize, overlap, result);
            }
        }
    }

    private static void ForceSplit(
        string text,
        int globalOffset,
        int chunkSize,
        int overlap,
        List<TextChunk> result)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            var end = Math.Min(pos + chunkSize, text.Length);
            var chunk = text[pos..end];

            result.Add(new TextChunk(
                Content: chunk,
                StartIndex: globalOffset + pos,
                EndIndex: globalOffset + end));

            var advance = chunkSize - overlap;
            if (advance <= 0) advance = 1; // prevent infinite loop
            pos += advance;

            // Avoid creating a tiny trailing chunk that's entirely overlap
            if (pos < text.Length && text.Length - pos <= overlap)
            {
                var remaining = text[pos..];
                result.Add(new TextChunk(
                    Content: remaining,
                    StartIndex: globalOffset + pos,
                    EndIndex: globalOffset + text.Length));
                break;
            }
        }
    }

    /// <summary>
    /// Splits text by separator and returns (part, offset) pairs.
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

            // Include the separator with the preceding part
            var endPos = idx + separator.Length;
            parts.Add((text[startPos..endPos], startPos));
            startPos = endPos;
        }

        return parts;
    }

    /// <summary>
    /// Merges adjacent small parts until they approach chunkSize, respecting overlap.
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
                // Emit current chunk
                merged.Add((currentText.ToString(), currentOffset));

                // Start new chunk, potentially with overlap
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
