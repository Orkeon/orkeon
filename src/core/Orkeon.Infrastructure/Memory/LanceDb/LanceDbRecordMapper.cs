using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Constants.Serialization;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Shared JSON options and row ⇆ domain mapping for the LanceDB provider.
/// Rows travel to and from the server as Arrow IPC columns
/// (see <see cref="LanceDbArrowCodec"/>); only the free-form payloads
/// (tags, custom metadata) are JSON-encoded inside string columns.
/// </summary>
internal static class LanceDbRecordMapper
{
    /// <summary>JSON options shared for tags/metadata column payloads.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>Serializes tags to a JSON array string, or <see langword="null"/> when empty.</summary>
    public static string? SerializeTags(string[]? tags)
        => tags is { Length: > 0 } ? JsonSerializer.Serialize(tags, JsonOptions) : null;

    /// <summary>Deserializes a JSON array string back to tags, tolerating malformed payloads.</summary>
    public static string[]? DeserializeTags(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Represents a single row of the remote LanceDB table.
/// </summary>
internal sealed class LanceDbRecord
{
    public string Id { get; set; } = "";

    public string Content { get; set; } = "";

    public float[]? Embedding { get; set; }

    public float Importance { get; set; } = MemoryDefaults.DefaultImportance;

    public string Source { get; set; } = "unknown";

    public string[]? Tags { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? MetadataJson { get; set; }

    /// <summary>
    /// Creates a <see cref="LanceDbRecord"/> from a <see cref="MemoryItem"/>.
    /// </summary>
    public static LanceDbRecord FromMemoryItem(string key, MemoryItem item)
    {
        return new LanceDbRecord
        {
            Id = key,
            Content = item.Content,
            Embedding = item.Embedding?.ToArray(),
            Importance = item.Importance,
            Source = item.Source,
            Tags = item.Tags.Count > 0 ? item.Tags.ToArray() : null,
            CreatedAt = item.Timestamp,
            MetadataJson = item.Metadata.CustomProperties != null
                ? JsonSerializer.Serialize(item.Metadata.CustomProperties, LanceDbRecordMapper.JsonOptions)
                : null
        };
    }

    /// <summary>
    /// Converts this record back to a <see cref="MemoryItem"/>.
    /// </summary>
    public MemoryItem ToMemoryItem()
    {
        Dictionary<string, string>? customProps = null;
        if (!string.IsNullOrEmpty(MetadataJson))
        {
            try
            {
                customProps = JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson, LanceDbRecordMapper.JsonOptions);
            }
            catch (JsonException)
            {
                // Ignore deserialization failures for metadata
            }
        }

        return MemoryItem.Create(
            Content,
            Embedding,
            Importance,
            Source,
            Tags,
            customProperties: customProps);
    }
}
