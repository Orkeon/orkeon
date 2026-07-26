using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Retrieval;

/// <summary>
/// Maps <see cref="MemoryItem"/>s written by <c>MemoryProviderDocumentStore</c> back to
/// <see cref="Chunk"/>s for the native hybrid path of
/// <see cref="HybridSearchDocumentStore"/> (RAG-04/C2).
/// </summary>
/// <remarks>
/// The <c>rag.*</c> property names mirror the private constants of
/// <c>Stores/MemoryProviderDocumentStore</c> (frozen for this lot) — keep both in sync.
/// </remarks>
internal static class RagChunkMemoryMapper
{
    internal const string KindProperty = "rag.kind";
    internal const string CollectionProperty = "rag.collection";
    internal const string ChunkKind = "chunk";
    internal const string MetadataPrefix = "meta.";

    private const string DocumentIdProperty = "rag.document_id";
    private const string ChunkIdProperty = "rag.chunk_id";
    private const string IndexProperty = "rag.index";
    private const string StartOffsetProperty = "rag.start_offset";
    private const string EndOffsetProperty = "rag.end_offset";

    /// <summary>
    /// Converts scored memory items into <see cref="ScoredChunk"/>s carrying
    /// <paramref name="scoreOrigin"/>. Malformed chunk entries fail loudly (same contract
    /// as the document store: entries written by the store are always well-formed).
    /// </summary>
    internal static IReadOnlyList<ScoredChunk> ToScoredChunks(
        IReadOnlyList<ScoredMemoryItem> hits,
        string scoreOrigin)
    {
        return hits
            .Select(hit => new ScoredChunk
            {
                Chunk = ToChunk(hit.Item),
                Score = hit.Score,
                ScoreOrigin = scoreOrigin,
            })
            .ToList();
    }

    /// <summary>Rebuilds the <see cref="Chunk"/> from a memory item's <c>rag.*</c> properties.</summary>
    internal static Chunk ToChunk(MemoryItem item)
    {
        var properties = item.Metadata.CustomProperties
            ?? throw CorruptEntry(item, KindProperty);

        var metadata = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (key, value) in properties)
        {
            if (key.StartsWith(MetadataPrefix, StringComparison.Ordinal))
                metadata[key[MetadataPrefix.Length..]] = value;
        }

        return new Chunk
        {
            Id = RequireProperty(properties, ChunkIdProperty, item),
            DocumentId = RequireProperty(properties, DocumentIdProperty, item),
            SourceId = item.Source,
            Content = item.Content,
            Index = RequireInt(properties, IndexProperty, item),
            StartOffset = RequireInt(properties, StartOffsetProperty, item),
            EndOffset = RequireInt(properties, EndOffsetProperty, item),
            Metadata = metadata.ToImmutable(),
        };
    }

    private static string RequireProperty(
        Dictionary<string, string> properties, string name, MemoryItem item)
    {
        return properties.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : throw CorruptEntry(item, name);
    }

    private static int RequireInt(
        Dictionary<string, string> properties, string name, MemoryItem item)
    {
        return properties.TryGetValue(name, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw CorruptEntry(item, name);
    }

    private static InvalidOperationException CorruptEntry(MemoryItem item, string property)
    {
        return new InvalidOperationException(
            $"Memory item '{item.Id}' is not a valid RAG chunk entry: " +
            $"missing or invalid property '{property}'.");
    }
}
