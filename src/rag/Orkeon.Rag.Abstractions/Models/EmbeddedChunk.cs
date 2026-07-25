using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// A <see cref="Models.Chunk"/> paired with its embedding vector, ready for upsert
/// into an <see cref="Interfaces.IDocumentStore"/>.
/// </summary>
public sealed record EmbeddedChunk
{
    /// <summary>The chunk being embedded.</summary>
    public required Chunk Chunk { get; init; }

    /// <summary>Embedding vector of <see cref="Chunk"/>'s content.</summary>
    public required ImmutableArray<float> Embedding { get; init; }

    /// <summary>
    /// Identifier of the embedding model that produced <see cref="Embedding"/>
    /// (model drift within a collection must fail hard, never reindex silently).
    /// </summary>
    public string? EmbeddingModel { get; init; }
}
