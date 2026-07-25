namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Provenance of a cited passage in a <see cref="RagAnswer"/>: links a <c>[n]</c>
/// marker in the answer text to the scored chunk it came from.
/// </summary>
public sealed record Citation
{
    /// <summary>Citation marker number as it appears in the answer text (<c>[n]</c>).</summary>
    public required int Marker { get; init; }

    /// <summary>Identifier of the cited chunk.</summary>
    public required string ChunkId { get; init; }

    /// <summary>Identity of the originating source.</summary>
    public required string SourceId { get; init; }

    /// <summary>Identifier of the parent document, when known.</summary>
    public string? DocumentId { get; init; }

    /// <summary>Short excerpt of the cited content.</summary>
    public string? Snippet { get; init; }

    /// <summary>Relevance score the chunk carried when it was cited.</summary>
    public double Score { get; init; }

    /// <summary>Inclusive character offset of the passage start in the source document.</summary>
    public int StartOffset { get; init; }

    /// <summary>Exclusive character offset of the passage end in the source document.</summary>
    public int EndOffset { get; init; }
}
