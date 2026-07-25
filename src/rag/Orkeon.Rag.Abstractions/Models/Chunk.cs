using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// A contiguous slice of a <see cref="RagDocument"/> produced by an
/// <see cref="Interfaces.IChunkingStrategy"/>. Offsets locate the chunk in the
/// original document content for citation purposes.
/// </summary>
public sealed record Chunk
{
    /// <summary>Unique chunk identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Identifier of the parent <see cref="RagDocument"/>.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Identity of the originating source (denormalized for deletion and citations).</summary>
    public required string SourceId { get; init; }

    /// <summary>Text content of the chunk.</summary>
    public required string Content { get; init; }

    /// <summary>Zero-based position of the chunk within its document.</summary>
    public int Index { get; init; }

    /// <summary>Inclusive character offset of the chunk start in the document content.</summary>
    public int StartOffset { get; init; }

    /// <summary>Exclusive character offset of the chunk end in the document content.</summary>
    public int EndOffset { get; init; }

    /// <summary>Metadata inherited from the document plus chunk-level annotations.</summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
