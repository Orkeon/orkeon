namespace Orkeon.Domain.Memory;

/// <summary>
/// Domain service that encapsulates the business rule for ranking memory items by relevance.
/// Items are ordered by importance (descending), then by recency of last access (descending).
/// </summary>
internal static class MemoryRelevanceService
{
    /// <summary>
    /// Ranks memory items by relevance using importance-first, recency-second ordering,
    /// and returns the top <paramref name="maxResults"/> items.
    /// </summary>
    /// <param name="items">The memory items to rank.</param>
    /// <param name="maxResults">The maximum number of items to return.</param>
    /// <returns>A read-only list of ranked memory items.</returns>
    public static IReadOnlyList<MemoryItem> RankByRelevance(IEnumerable<MemoryItem> items, int maxResults)
    {
        return items
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.LastAccessed)
            .Take(maxResults)
            .ToList();
    }
}
