using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Memory;

namespace Orkeon.Analysis.Vectors;

public sealed class MemoryProviderVectorStoreAdapter : IVectorStoreProvider
{
    private const string MetadataPrefix = "rt.meta.";
    private const string KeyPrefix = "raggable-tree::";

    private readonly IMemoryProvider _memoryProvider;

    public MemoryProviderVectorStoreAdapter(IMemoryProvider memoryProvider)
    {
        _memoryProvider = memoryProvider ?? throw new ArgumentNullException(nameof(memoryProvider));
    }

    public Task IndexAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return IndexCoreAsync(documents, ct);
    }

    private async Task IndexCoreAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct)
    {
        foreach (var doc in documents)
        {
            ct.ThrowIfCancellationRequested();
            var item = MemoryItem.Create(
                content: doc.Text,
                tags: [.. doc.Metadata.Tags],
                customProperties: BuildCustomProperties(doc));
            var key = ToKey(doc.Id);
            var embedding = doc.Embedding.ToArray();
            await _memoryProvider.StoreWithEmbeddingAsync(key, item, embedding, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        string queryText,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        IReadOnlyDictionary<string, object>? filters,
        CancellationToken ct)
    {
        if (topK <= 0) return [];

        var filterDict = filters is null
            ? null
            : new Dictionary<string, object>(filters, StringComparer.Ordinal);

        var scored = await _memoryProvider.SearchSimilarAsync(
            queryEmbedding.ToArray(), topK, 0.0f, filterDict, ct).ConfigureAwait(false);

        var hits = new List<VectorSearchHit>(scored.Count);
        foreach (var s in scored)
        {
            var metadata = RestoreMetadata(s.Item);
            if (filters is not null && !PassesFilter(metadata, filters)) continue;

            var docId = RestoreId(s.Item);
            hits.Add(new VectorSearchHit(docId, s.Item.Content, s.Score, metadata));
        }
        return hits;
    }

    public Task DeleteAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return DeleteCoreAsync(ids, ct);
    }

    private async Task DeleteCoreAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            await _memoryProvider.DeleteAsync(ToKey(id), ct).ConfigureAwait(false);
        }
    }

    private static string ToKey(string id) => KeyPrefix + id;

    private static Dictionary<string, string> BuildCustomProperties(VectorDocument doc)
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MetadataPrefix + "id"] = doc.Id,
            [MetadataPrefix + "kind"] = doc.Metadata.Kind,
            [MetadataPrefix + "language"] = doc.Metadata.Language,
            [MetadataPrefix + "virtualFilePath"] = doc.Metadata.VirtualFilePath,
            [MetadataPrefix + "fqn"] = doc.Metadata.Fqn,
            [MetadataPrefix + "hasParent"] = doc.Metadata.HasParent ? "1" : "0",
            [MetadataPrefix + "childCount"] = doc.Metadata.ChildCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [MetadataPrefix + "inDegree"] = doc.Metadata.InDegree.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [MetadataPrefix + "outDegree"] = doc.Metadata.OutDegree.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [MetadataPrefix + "tags"] = string.Join("|", doc.Metadata.Tags),
            [MetadataPrefix + "decorators"] = string.Join("|", doc.Metadata.Decorators),
        };
        return props;
    }

    private static VectorMetadata RestoreMetadata(MemoryItem item)
    {
        var props = item.Metadata.CustomProperties;
        return new VectorMetadata
        {
            Kind = Read(props, "kind"),
            Language = Read(props, "language"),
            VirtualFilePath = Read(props, "virtualFilePath"),
            Fqn = Read(props, "fqn"),
            Tags = SplitPipe(Read(props, "tags")),
            Decorators = SplitPipe(Read(props, "decorators")),
            HasParent = Read(props, "hasParent") == "1",
            ChildCount = ParseInt(Read(props, "childCount")),
            InDegree = ParseInt(Read(props, "inDegree")),
            OutDegree = ParseInt(Read(props, "outDegree")),
        };
    }

    private static string RestoreId(MemoryItem item)
    {
        return Read(item.Metadata.CustomProperties, "id");
    }

    private static bool PassesFilter(VectorMetadata metadata, IReadOnlyDictionary<string, object> filters)
    {
        foreach (var (key, value) in filters)
        {
            var expected = value?.ToString() ?? string.Empty;
            var actual = key switch
            {
                "kind" => metadata.Kind,
                "language" => metadata.Language,
                "virtualFilePath" => metadata.VirtualFilePath,
                "fqn" => metadata.Fqn,
                _ => null,
            };
            if (actual is null) continue;
            if (!string.Equals(actual, expected, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static string Read(Dictionary<string, string>? props, string suffix)
    {
        if (props is null) return string.Empty;
        return props.TryGetValue(MetadataPrefix + suffix, out var v) ? v : string.Empty;
    }

    private static ImmutableArray<string> SplitPipe(string joined)
    {
        if (string.IsNullOrEmpty(joined)) return [];
        return [.. joined.Split('|', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
