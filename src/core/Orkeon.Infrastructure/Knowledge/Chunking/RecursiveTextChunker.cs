using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Rag.Chunking;

namespace Orkeon.Infrastructure.Knowledge.Chunking;

/// <summary>
/// Splits text using a hierarchy of separators, recursively trying finer-grained
/// separators when chunks are still too large.
/// Default separator hierarchy: paragraph breaks, line breaks, sentences, words, characters.
/// </summary>
/// <remarks>
/// Thin delegation to the canonical <see cref="RecursiveChunkingStrategy"/> in
/// <c>Orkeon.Rag</c> (RAG-02/C3 consolidation). This type is kept until the
/// <c>Orkeon.Infrastructure.Knowledge</c> namespace is removed (RAG-02/C5).
/// </remarks>
public sealed class RecursiveTextChunker : ITextChunker
{
    /// <inheritdoc />
    public IReadOnlyList<TextChunk> Chunk(string text, ChunkingOptions? options = null)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<TextChunk>();

        options ??= new ChunkingOptions();
        var strategy = new RecursiveChunkingStrategy(options.Separators);

        var chunks = strategy.ChunkText(text, Math.Max(1, options.ChunkSize), Math.Max(0, options.ChunkOverlap));
        return [.. chunks.Select(c => new TextChunk(c.Content, c.StartOffset, c.EndOffset))];
    }
}
