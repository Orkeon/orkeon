using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Splits a <see cref="RagDocument"/> into <see cref="Chunk"/>s. Named strategies
/// (<c>recursive</c>, <c>sentence</c>, <c>structural</c>, <c>semantic</c>, <c>fixed</c>)
/// are resolved by the chunking factory in <c>Orkeon.Rag</c>.
/// </summary>
public interface IChunkingStrategy
{
    /// <summary>Strategy name used for factory resolution (lower-case, e.g. <c>recursive</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Version of the chunking algorithm, recorded in the collection ingestion
    /// manifest. Bump it whenever the strategy starts producing different chunks
    /// for identical inputs — incremental ingestion re-ingests every source whose
    /// recorded chunker version differs from the current one. Defaults to <c>"1"</c>.
    /// </summary>
    string Version => "1";

    /// <summary>Splits <paramref name="document"/> into ordered chunks.</summary>
    /// <param name="document">The document to split.</param>
    /// <param name="options">Chunking parameters (size, overlap, strategy-specific knobs).</param>
    IReadOnlyList<Chunk> Chunk(RagDocument document, ChunkingOptions options);
}
