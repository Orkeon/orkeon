using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Chunking;

/// <summary>
/// Convenience entry points for callers that chunk raw text outside a full RAG
/// ingestion pipeline (e.g. search tools chunking a file they just read). Wraps the
/// text in an ad-hoc <see cref="RagDocument"/> and exposes offset→line mapping for
/// tools that report approximate line numbers.
/// </summary>
public static class PlainTextChunking
{
    /// <summary>
    /// Chunks <paramref name="text"/> with <paramref name="strategy"/> using an ad-hoc
    /// document identified by <paramref name="sourceId"/>. Returns an empty list for
    /// null/whitespace text.
    /// </summary>
    public static IReadOnlyList<Chunk> ChunkText(
        this IChunkingStrategy strategy,
        string? text,
        int maxChunkSize,
        int overlap = 0,
        string sourceId = "inline")
    {
        ArgumentNullException.ThrowIfNull(strategy);

        if (string.IsNullOrWhiteSpace(text))
            return [];

        var document = new RagDocument
        {
            Id = sourceId,
            SourceId = sourceId,
            Content = text
        };

        return strategy.Chunk(document, new ChunkingOptions
        {
            MaxChunkSize = maxChunkSize,
            Overlap = overlap
        });
    }

    /// <summary>
    /// Returns the 1-based line number of <paramref name="offset"/> in
    /// <paramref name="text"/> (the number of <c>'\n'</c> characters before the offset,
    /// plus one). Offsets are clamped to the text bounds.
    /// </summary>
    public static int LineNumberAt(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);

        int line = 1;
        int limit = Math.Clamp(offset, 0, text.Length);
        for (int i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }
}
