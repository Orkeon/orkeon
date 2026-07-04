using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Knowledge.Chunking;

/// <summary>
/// Splits text on sentence boundaries and groups sentences into chunks
/// that respect size limits.
/// </summary>
public sealed class SentenceChunker : ITextChunker
{
    private static readonly string[] DefaultSentenceEndings = [". ", "! ", "? ", ".\n", "!\n", "?\n"];

    /// <inheritdoc />
    public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<TextChunk>();

        options ??= new ChunkingOptions();
        var chunkSize = Math.Max(1, options.ChunkSize);
        var overlap = Math.Max(0, Math.Min(options.ChunkOverlap, chunkSize - 1));

        var sentences = SplitIntoSentences(text);

        if (sentences.Count == 0)
            return Array.Empty<TextChunk>();

        var chunks = new List<TextChunk>();
        GroupSentences(sentences, chunkSize, overlap, chunks);
        return chunks.AsReadOnly();
    }

    /// <summary>
    /// Splits text into sentences, preserving their positions.
    /// </summary>
    private static List<(string Text, int Offset)> SplitIntoSentences(string text)
    {
        var sentences = new List<(string Text, int Offset)>();
        int pos = 0;

        while (pos < text.Length)
        {
            int nearestEnd = -1;
            int endingLength = 0;

            foreach (var ending in DefaultSentenceEndings)
            {
                var idx = text.IndexOf(ending, pos, StringComparison.Ordinal);
                if (idx >= 0 && (nearestEnd < 0 || idx < nearestEnd))
                {
                    nearestEnd = idx;
                    endingLength = ending.Length;
                }
            }

            if (nearestEnd < 0)
            {
                // No more sentence endings; take remaining text
                var remaining = text[pos..];
                if (!string.IsNullOrWhiteSpace(remaining))
                    sentences.Add((remaining, pos));
                break;
            }

            var sentenceEnd = nearestEnd + endingLength;
            var sentence = text[pos..sentenceEnd];
            if (!string.IsNullOrWhiteSpace(sentence))
                sentences.Add((sentence, pos));
            pos = sentenceEnd;
        }

        return sentences;
    }

    /// <summary>
    /// Groups sentences into chunks respecting size limits and overlap.
    /// </summary>
    private static void GroupSentences(
        List<(string Text, int Offset)> sentences,
        int chunkSize,
        int overlap,
        List<TextChunk> result)
    {
        int i = 0;

        while (i < sentences.Count)
        {
            int j = CollectSentencesForChunk(sentences, i, chunkSize, out var chunkText, out int sentencesInChunk);

            int chunkStart = sentences[i].Offset;
            int chunkEnd = chunkStart + chunkText.Length;

            result.Add(new TextChunk(
                Content: chunkText,
                StartIndex: chunkStart,
                EndIndex: chunkEnd));

            if (j >= sentences.Count)
                break;

            i = CalculateNextStart(sentences, i, j, sentencesInChunk, overlap);
        }
    }

    /// <summary>
    /// Collects sentences starting from index i until chunk size is reached.
    /// Returns the index after the last sentence included.
    /// </summary>
    private static int CollectSentencesForChunk(
        List<(string Text, int Offset)> sentences,
        int start,
        int chunkSize,
        out string chunkText,
        out int sentencesInChunk)
    {
        var chunkBuilder = new System.Text.StringBuilder();
        sentencesInChunk = 0;

        int j = start;
        while (j < sentences.Count)
        {
            var candidate = sentences[j].Text;

            if (chunkBuilder.Length > 0 && chunkBuilder.Length + candidate.Length > chunkSize)
                break;

            chunkBuilder.Append(candidate);
            sentencesInChunk++;
            j++;

            if (chunkBuilder.Length >= chunkSize)
                break;
        }

        chunkText = chunkBuilder.ToString();
        return j;
    }

    /// <summary>
    /// Calculates the next starting sentence index accounting for overlap.
    /// </summary>
    private static int CalculateNextStart(
        List<(string Text, int Offset)> sentences,
        int chunkStart,
        int chunkEnd,
        int sentencesInChunk,
        int overlap)
    {
        if (overlap <= 0 || sentencesInChunk <= 1)
            return chunkEnd;

        int overlapChars = 0;
        int overlapSentences = 0;
        for (int k = chunkEnd - 1; k >= chunkStart; k--)
        {
            overlapChars += sentences[k].Text.Length;
            overlapSentences++;
            if (overlapChars >= overlap)
                break;
        }

        int nextStart = chunkEnd - overlapSentences;

        // Ensure at least 1 sentence advance
        if (nextStart <= (chunkEnd - sentencesInChunk))
            nextStart = chunkEnd - sentencesInChunk + 1;

        if (nextStart < 0)
            nextStart = 0;

        // Ensure forward progress
        if (nextStart <= (chunkEnd - sentencesInChunk))
            nextStart = chunkEnd;

        return nextStart;
    }
}
