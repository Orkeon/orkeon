using Orkeon.Domain.Memory;
using Orkeon.Application.Memory;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Services.Memory
{
    /// <summary>
    /// Result from a memory search operation
    /// </summary>
    public class MemorySearchResult
    {
        /// <summary>
        /// Unique identifier of the memory item
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Content of the memory
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Similarity score (0-1)
        /// </summary>
        public float Score { get; set; }

        /// <summary>
        /// Metadata associated with the memory
        /// </summary>
        public MemorySearchMetadata Metadata { get; set; } = MemorySearchMetadata.Empty;

        /// <summary>
        /// Timestamp when the memory was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Agent ID that created this memory
        /// </summary>
        public string? AgentId { get; set; }

        /// <summary>
        /// Context in which the memory was created
        /// </summary>
        public string? Context { get; set; }

        /// <summary>
        /// Type of memory
        /// </summary>
        public Orkeon.Domain.Memory.MemoryType Type { get; set; }

        /// <summary>
        /// The embedding vector (optional)
        /// </summary>
        public IReadOnlyList<float>? Embedding { get; set; }

        /// <summary>
        /// Convert to MemoryItem
        /// </summary>
        public MemoryItem ToMemoryItem()
        {
            var builder = MemorySearchMetadata.CreateBuilder();

            // Copy existing metadata
            foreach (var key in Metadata.Keys)
            {
                var value = Metadata.Get<object>(key);
                if (value != null)
                    builder.Add(key, value);
            }

            // Add additional properties
            builder.AddType(Type.ToString());
            if (!string.IsNullOrEmpty(AgentId))
                builder.AddAgentId(AgentId);
            if (!string.IsNullOrEmpty(Context))
                builder.AddContext(Context);

            var finalMetadata = builder.Build();

            return MemoryItem.Create(
                content: Content,
                embedding: Embedding,
                importance: MemoryDefaults.DefaultImportance,
                source: "search_result",
                tags: null,
                createdBy: null,
                customProperties: finalMetadata.ToStringDictionary());
        }

        /// <summary>
        /// Create from MemoryItem
        /// </summary>
        public static MemorySearchResult FromMemoryItem(MemoryItem item, float score)
        {
            ArgumentNullException.ThrowIfNull(item);
            var metadataBuilder = MemorySearchMetadata.CreateBuilder()
                .AddSource(item.Metadata.Source)
                .AddRelevance((float)item.Metadata.Relevance)
                .AddAccessCount(item.Metadata.AccessCount);

            var result = new MemorySearchResult
            {
                Id = item.Id,
                Content = item.Content,
                Score = score,
                Metadata = metadataBuilder.Build(),
                CreatedAt = item.Timestamp,
                AgentId = item.Metadata.CreatedBy?.ToString(),
                Context = item.Metadata.CustomProperties?.GetValueOrDefault("context"),
                Embedding = item.Embedding?.ToArray()
            };

            // Try to parse memory type from custom properties
            if (item.Metadata.CustomProperties?.TryGetValue("type", out var typeStr) == true && typeStr != null
                && Enum.TryParse<Orkeon.Domain.Memory.MemoryType>(typeStr, out var type))
            {
                result.Type = type;
            }

            return result;
        }
    }
}
