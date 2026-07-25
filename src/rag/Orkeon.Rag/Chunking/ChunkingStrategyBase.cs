using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Chunking;

/// <summary>
/// Shared pipeline for the built-in <see cref="IChunkingStrategy"/> implementations:
/// argument guards, option normalization (<see cref="ChunkingOptions.MaxChunkSize"/> ≥ 1,
/// <see cref="ChunkingOptions.Overlap"/> clamped to <c>[0, MaxChunkSize - 1]</c>),
/// whitespace trimming with offset adjustment, and <see cref="Chunk"/> materialization.
/// </summary>
/// <remarks>
/// Invariant guaranteed for every produced chunk:
/// <c>document.Content[chunk.StartOffset..chunk.EndOffset] == chunk.Content</c>.
/// Whitespace-only documents and whitespace-only slices produce no chunks.
/// Third parties should implement <see cref="IChunkingStrategy"/> directly; this base
/// class is an internal implementation detail of the built-in strategies.
/// </remarks>
public abstract class ChunkingStrategyBase : IChunkingStrategy
{
    /// <summary>Metadata key carrying the structural heading a chunk belongs to.</summary>
    public const string HeadingMetadataKey = "heading";

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<Chunk> Chunk(RagDocument document, ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var content = document.Content;
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var maxChunkSize = Math.Max(1, options.MaxChunkSize);
        var overlap = Math.Max(0, Math.Min(options.Overlap, maxChunkSize - 1));

        var slices = ComputeSlices(content, maxChunkSize, overlap, options);

        var chunks = new List<Chunk>(slices.Count);
        var index = 0;
        foreach (var slice in slices)
        {
            var start = slice.Start;
            var end = slice.End;

            // Trim surrounding whitespace while keeping offsets coherent with the content.
            while (start < end && char.IsWhiteSpace(content[start]))
                start++;
            while (end > start && char.IsWhiteSpace(content[end - 1]))
                end--;

            if (start >= end)
                continue;

            var metadata = document.Metadata;
            if (!string.IsNullOrEmpty(slice.Heading))
                metadata = metadata.SetItem(HeadingMetadataKey, slice.Heading);

            chunks.Add(new Chunk
            {
                Id = $"{document.Id}#{index}",
                DocumentId = document.Id,
                SourceId = document.SourceId,
                Content = content[start..end],
                Index = index,
                StartOffset = start,
                EndOffset = end,
                Metadata = metadata
            });
            index++;
        }

        return chunks;
    }

    /// <summary>
    /// Computes the raw slices for <paramref name="content"/>. Offsets are global
    /// (relative to the full document content); the base class trims and materializes.
    /// </summary>
    internal abstract List<ChunkSlice> ComputeSlices(
        string content, int maxChunkSize, int overlap, ChunkingOptions options);
}
