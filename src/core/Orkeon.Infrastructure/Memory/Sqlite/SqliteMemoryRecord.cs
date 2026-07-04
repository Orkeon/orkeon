using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Constants.Serialization;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Memory.ValueObjects;

namespace Orkeon.Infrastructure.Memory.Sqlite;

/// <summary>
/// Row-level DTO for the SQLite memory provider: maps a <see cref="MemoryItem"/>
/// to/from the <c>memory_items</c> table, including the embedding BLOB codec and
/// JSON-encoded tags/custom properties. Mirrors the LanceDb record-mapper convention.
/// </summary>
internal sealed class SqliteMemoryRecord
{
    /// <summary>JSON options shared by tag/custom-property (de)serialization.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    public string Key { get; set; } = "";

    public string ItemId { get; set; } = "";

    public string Content { get; set; } = "";

    public float[]? Embedding { get; set; }

    public float Importance { get; set; } = MemoryDefaults.DefaultImportance;

    public string Source { get; set; } = "unknown";

    public double Relevance { get; set; }

    public string? TagsJson { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastAccessedAt { get; set; }

    public int AccessCount { get; set; }

    public string? CustomPropertiesJson { get; set; }

    /// <summary>
    /// Creates a <see cref="SqliteMemoryRecord"/> from a <see cref="MemoryItem"/>.
    /// </summary>
    public static SqliteMemoryRecord FromMemoryItem(string key, MemoryItem item)
    {
        return new SqliteMemoryRecord
        {
            Key = key,
            ItemId = item.Id.AsString(),
            Content = item.Content,
            Embedding = item.Embedding?.ToArray(),
            Importance = item.Importance,
            Source = item.Source,
            Relevance = item.Metadata.Relevance,
            TagsJson = item.Tags.Count > 0
                ? JsonSerializer.Serialize(item.Tags, JsonOptions)
                : null,
            CreatedBy = item.Metadata.CreatedBy?.AsString(),
            CreatedAt = item.Metadata.CreatedAt,
            LastAccessedAt = item.Metadata.LastAccessedAt,
            AccessCount = item.Metadata.AccessCount,
            CustomPropertiesJson = item.Metadata.CustomProperties is { Count: > 0 }
                ? JsonSerializer.Serialize(item.Metadata.CustomProperties, JsonOptions)
                : null
        };
    }

    /// <summary>
    /// Rehydrates this record into a <see cref="MemoryItem"/>, preserving the original
    /// identifier and metadata via <see cref="MemoryItem.Restore"/> when the stored
    /// identifier is a valid ULID (falls back to <see cref="MemoryItem.Create"/> otherwise).
    /// </summary>
    public MemoryItem ToMemoryItem()
    {
        var tags = DeserializeOrNull<string[]>(TagsJson);
        var customProperties = DeserializeOrNull<Dictionary<string, string>>(CustomPropertiesJson);
        var createdBy = TryParseId<AgentId>(CreatedBy);

        if (TryParseId<MemoryItemId>(ItemId) is { } itemId)
        {
            var metadata = MemoryMetadata.Create(
                createdAt: CreatedAt,
                lastAccessedAt: LastAccessedAt,
                source: Source,
                relevance: Relevance,
                tags: tags,
                createdBy: createdBy,
                accessCount: AccessCount,
                customProperties: customProperties);

            return MemoryItem.Restore(itemId, Content, Embedding, Importance, metadata);
        }

        return MemoryItem.Create(
            Content,
            Embedding,
            Importance,
            Source,
            tags,
            createdBy,
            customProperties);
    }

    /// <summary>
    /// Checks whether this record matches the provided metadata filter
    /// (same semantics as the LanceDb search engine: <c>source</c>, <c>tag(s)</c>,
    /// then a substring probe into the custom-properties JSON).
    /// </summary>
    public bool MatchesFilter(Dictionary<string, object> filter)
    {
        foreach (var (key, value) in filter)
        {
            var filterValue = value?.ToString();

#pragma warning disable CA1308 // lowercase is the normalized filter-key token driving the switch, not a comparison normalization
            switch (key.ToLowerInvariant())
#pragma warning restore CA1308
            {
                case "source":
                    if (!string.Equals(Source, filterValue, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;

                case "tag" or "tags":
                    var tags = DeserializeOrNull<string[]>(TagsJson);
                    if (tags == null || !tags.Contains(filterValue!, StringComparer.OrdinalIgnoreCase))
                        return false;
                    break;

                default:
                    if (CustomPropertiesJson != null)
                    {
                        if (!CustomPropertiesJson.Contains(filterValue ?? "", StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    else
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Serializes an embedding vector to a little-endian float BLOB.
    /// </summary>
    public static byte[]? EmbeddingToBytes(float[]? embedding)
    {
        if (embedding == null || embedding.Length == 0)
            return null;

        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Deserializes a float BLOB back to an embedding vector.
    /// </summary>
    public static float[]? BytesToEmbedding(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
            return null;

        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static T? DeserializeOrNull<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Tolerate corrupted auxiliary JSON; the core columns remain usable.
            return null;
        }
    }

    private static TId? TryParseId<TId>(string? value)
        where TId : EntityId<TId>, new()
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Ulid.TryParse(value, out var ulid) && ulid != default
            ? EntityId<TId>.From(ulid)
            : null;
    }
}
