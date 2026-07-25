using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IChunkingStrategy"/> double for pipeline tests
/// (concrete strategies are consolidated in a parallel batch): splits the
/// document content into fixed-size slices of <see cref="ChunkingOptions.MaxChunkSize"/>
/// characters, no overlap, and records every call.
/// </summary>
public sealed class StubChunkingStrategy : IChunkingStrategy
{
    /// <inheritdoc />
    public string Name => "stub";

    /// <inheritdoc />
    /// <remarks>Settable so incremental-ingestion tests can simulate an algorithm bump.</remarks>
    public string Version { get; set; } = "1";

    /// <summary>Documents received, in call order.</summary>
    public List<RagDocument> ChunkedDocuments { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<Chunk> Chunk(RagDocument document, ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        ChunkedDocuments.Add(document);

        var chunks = new List<Chunk>();
        var content = document.Content;
        var size = Math.Max(1, options.MaxChunkSize);

        for (int offset = 0, index = 0; offset < content.Length; offset += size, index++)
        {
            var end = Math.Min(offset + size, content.Length);
            chunks.Add(new Chunk
            {
                Id = $"{document.Id}#{index}",
                DocumentId = document.Id,
                SourceId = document.SourceId,
                Content = content[offset..end],
                Index = index,
                StartOffset = offset,
                EndOffset = end,
                Metadata = document.Metadata,
            });
        }

        return chunks;
    }
}
