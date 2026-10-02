using Orkeon.Domain.Memory;

namespace Orkeon.Application.Memory;

/// <summary>
/// How a crew's memory is kept apart in a store it shares — with the other crews of its provider
/// type and with the RAG store of that type (GAP-20). Every entry of a crew's memory carries two
/// custom properties, <c>kind = crew-memory</c> and <c>crew = &lt;scope&gt;</c>, the scope being the
/// crew's name (its id when it has none), and every search of that memory asks the provider for
/// both. The run's memory (<see cref="MemoryCoordinator"/>) and the cognitive memory write and read
/// through the crew's long-term memory, which applies this schema: a crew has one memory (GAP-30).
/// </summary>
public static class CrewMemoryScope
{
    /// <summary>The custom property naming what an entry is.</summary>
    public const string KindProperty = "kind";

    /// <summary>The <see cref="KindProperty"/> of an entry of a crew's memory.</summary>
    public const string CrewMemoryKind = "crew-memory";

    /// <summary>The custom property naming whose memory an entry is: the crew's scope.</summary>
    public const string CrewProperty = "crew";

    /// <summary>
    /// The metadata filter that selects the memory of <paramref name="scope"/>, and nothing else,
    /// in a shared store — the filter of <see cref="IMemoryProvider.SearchAsync"/> and
    /// <see cref="IMemoryProvider.SearchSimilarAsync"/>, applied by every provider before its limit.
    /// </summary>
    /// <param name="scope">The crew's scope: its name, else its id.</param>
    public static Dictionary<string, object> Filter(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [KindProperty] = CrewMemoryKind,
            [CrewProperty] = scope,
        };
    }

    /// <summary>
    /// <paramref name="item"/> as an entry of the memory of <paramref name="scope"/>: the same item —
    /// id, content, embedding, importance, metadata — with <c>kind</c> and <c>crew</c> set. A copy,
    /// so the caller's item is left as it was built.
    /// </summary>
    /// <param name="item">The memory to stamp.</param>
    /// <param name="scope">The crew's scope: its name, else its id.</param>
    public static MemoryItem Stamp(MemoryItem item, string scope)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var properties = item.Metadata.CustomProperties is { } existing
            ? new Dictionary<string, string>(existing, existing.Comparer)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        properties[KindProperty] = CrewMemoryKind;
        properties[CrewProperty] = scope;

        return MemoryItem.Restore(
            item.Id, item.Content, item.Embedding, item.Importance, item.Metadata with { CustomProperties = properties });
    }
}
