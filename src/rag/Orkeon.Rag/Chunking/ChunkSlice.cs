namespace Orkeon.Rag.Chunking;

/// <summary>
/// A contiguous half-open character range <c>[Start, End)</c> of a document
/// selected by a chunking strategy, optionally annotated with the structural
/// heading it belongs to. Slices are materialized into
/// <see cref="Orkeon.Rag.Abstractions.Models.Chunk"/>s by
/// <see cref="ChunkingStrategyBase"/> (which trims surrounding whitespace and
/// drops empty slices).
/// </summary>
internal readonly record struct ChunkSlice(int Start, int End, string? Heading = null);
