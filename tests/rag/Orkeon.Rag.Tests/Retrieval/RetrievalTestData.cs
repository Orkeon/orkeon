using System.Collections.Immutable;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Retrieval;

/// <summary>Shared builders for the hybrid-retrieval tests.</summary>
internal static class RetrievalTestData
{
    internal static Chunk MakeChunk(string id, string content, string sourceId = "source.md") => new()
    {
        Id = id,
        DocumentId = sourceId,
        SourceId = sourceId,
        Content = content,
    };

    internal static ScoredChunk MakeScored(Chunk chunk, double score, string origin = "vector") => new()
    {
        Chunk = chunk,
        Score = score,
        ScoreOrigin = origin,
    };

    internal static EmbeddedChunk MakeEmbedded(Chunk chunk) => new()
    {
        Chunk = chunk,
        Embedding = ImmutableArray.Create(1f, 0f),
    };

    /// <summary>
    /// Builds a memory item shaped like a chunk entry written by the document store
    /// (the <c>rag.*</c> properties the native-path mapper reads back).
    /// </summary>
    internal static MemoryItem ChunkMemoryItem(string chunkId, string content, string sourceId = "source.md")
    {
        return MemoryItem.Create(
            content,
            source: sourceId,
            customProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rag.kind"] = "chunk",
                ["rag.collection"] = "docs",
                ["rag.document_id"] = sourceId,
                ["rag.chunk_id"] = chunkId,
                ["rag.index"] = "0",
                ["rag.start_offset"] = "0",
                ["rag.end_offset"] = content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["meta.lang"] = "fr",
            });
    }
}
