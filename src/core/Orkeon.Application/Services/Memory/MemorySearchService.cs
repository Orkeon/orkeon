using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory.Queries.SearchMemory;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Services.Memory;

/// <summary>
/// Searches and filters memory items within an agent memory store.
/// Handles tier selection, keyword matching, ordering by importance/recency, and result limiting.
/// </summary>
public sealed class MemorySearchService : IMemorySearchService
{
    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<MemoryItemDto>> SearchAsync(
        AgentMemoryStore store,
        string searchTerm,
        MemoryType? memoryType,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var candidates = new List<(MemoryItem Item, bool IsLongTerm)>();

        // Collect items from the requested memory tier(s)
        if (!memoryType.HasValue || memoryType.Value == MemoryType.ShortTerm)
        {
            foreach (var item in store.ShortTermMemory)
                candidates.Add((item, IsLongTerm: false));
        }

        if (!memoryType.HasValue || memoryType.Value == MemoryType.LongTerm)
        {
            foreach (var item in store.LongTermMemory)
                candidates.Add((item, IsLongTerm: true));
        }

        // Filter by search term (case-insensitive keyword match)
        var trimmedTerm = (searchTerm ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(trimmedTerm))
        {
            candidates = candidates
                .Where(c => c.Item.Content.Contains(trimmedTerm, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Order by importance descending, then by recency, and cap at MaxResults
        IReadOnlyList<MemoryItemDto> results = candidates
            .OrderByDescending(c => c.Item.Importance)
            .ThenByDescending(c => c.Item.Timestamp)
            .Take(maxResults)
            .Select(c => new MemoryItemDto(
                Id: c.Item.Id.ToString(),
                Content: c.Item.Content,
                Importance: c.Item.Importance,
                Source: c.Item.Source,
                Tags: c.Item.Tags.ToArray(),
                Timestamp: c.Item.Timestamp,
                IsLongTerm: c.IsLongTerm))
            .ToList();

        return System.Threading.Tasks.Task.FromResult(results);
    }
}
