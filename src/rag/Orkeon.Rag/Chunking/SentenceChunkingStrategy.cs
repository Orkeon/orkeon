using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Chunking;

/// <summary>
/// Splits text on sentence boundaries and groups consecutive sentences into chunks
/// that respect <see cref="ChunkingOptions.MaxChunkSize"/>, with sentence-aligned
/// <see cref="ChunkingOptions.Overlap"/> between consecutive chunks.
/// </summary>
/// <remarks>
/// Canonical port of the historical legacy Infrastructure <c>SentenceChunker</c>
/// (RAG-02/C3 consolidation). A single sentence longer than <c>MaxChunkSize</c> is emitted
/// whole — this strategy never splits inside a sentence.
/// </remarks>
public sealed class SentenceChunkingStrategy : ChunkingStrategyBase
{
    internal static readonly string[] DefaultSentenceEndings = [". ", "! ", "? ", ".\n", "!\n", "?\n"];

    /// <inheritdoc />
    public override string Name => "sentence";

    internal override List<ChunkSlice> ComputeSlices(
        string content, int maxChunkSize, int overlap, ChunkingOptions options)
    {
        var sentences = SplitIntoSentences(content);
        if (sentences.Count == 0)
            return [];

        var slices = new List<ChunkSlice>();
        GroupSentences(sentences, maxChunkSize, overlap, slices);
        return slices;
    }

    /// <summary>
    /// Splits text into sentences, preserving their global positions. Sentences tile
    /// the input contiguously (each sentence keeps its terminator and trailing space).
    /// Shared with <see cref="SemanticChunkingStrategy"/>.
    /// </summary>
    internal static List<(int Offset, int Length)> SplitIntoSentences(string text)
    {
        var sentences = new List<(int Offset, int Length)>();
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
                // No more sentence endings; take the remaining text.
                if (!IsWhiteSpaceRange(text, pos, text.Length))
                    sentences.Add((pos, text.Length - pos));
                break;
            }

            var sentenceEnd = nearestEnd + endingLength;
            if (!IsWhiteSpaceRange(text, pos, sentenceEnd))
                sentences.Add((pos, sentenceEnd - pos));
            pos = sentenceEnd;
        }

        return sentences;
    }

    private static bool IsWhiteSpaceRange(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Groups sentences into slices respecting size limits and overlap.
    /// </summary>
    private static void GroupSentences(
        List<(int Offset, int Length)> sentences,
        int chunkSize,
        int overlap,
        List<ChunkSlice> result)
    {
        int i = 0;

        while (i < sentences.Count)
        {
            int j = CollectSentencesForChunk(sentences, i, chunkSize, out int chunkLength, out int sentencesInChunk);

            int chunkStart = sentences[i].Offset;
            result.Add(new ChunkSlice(chunkStart, chunkStart + chunkLength));

            if (j >= sentences.Count)
                break;

            i = CalculateNextStart(sentences, i, j, sentencesInChunk, overlap);
        }
    }

    /// <summary>
    /// Collects sentences starting from <paramref name="start"/> until the chunk size is
    /// reached. Returns the index after the last sentence included.
    /// </summary>
    private static int CollectSentencesForChunk(
        List<(int Offset, int Length)> sentences,
        int start,
        int chunkSize,
        out int chunkLength,
        out int sentencesInChunk)
    {
        chunkLength = 0;
        sentencesInChunk = 0;

        int j = start;
        while (j < sentences.Count)
        {
            var candidateLength = sentences[j].Length;

            if (chunkLength > 0 && chunkLength + candidateLength > chunkSize)
                break;

            chunkLength += candidateLength;
            sentencesInChunk++;
            j++;

            if (chunkLength >= chunkSize)
                break;
        }

        return j;
    }

    /// <summary>
    /// Calculates the next starting sentence index accounting for overlap,
    /// always guaranteeing forward progress.
    /// </summary>
    private static int CalculateNextStart(
        List<(int Offset, int Length)> sentences,
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
            overlapChars += sentences[k].Length;
            overlapSentences++;
            if (overlapChars >= overlap)
                break;
        }

        int nextStart = chunkEnd - overlapSentences;

        // Ensure at least one sentence of advance.
        if (nextStart <= (chunkEnd - sentencesInChunk))
            nextStart = chunkEnd - sentencesInChunk + 1;

        if (nextStart < 0)
            nextStart = 0;

        // Ensure forward progress.
        if (nextStart <= (chunkEnd - sentencesInChunk))
            nextStart = chunkEnd;

        return nextStart;
    }
}
