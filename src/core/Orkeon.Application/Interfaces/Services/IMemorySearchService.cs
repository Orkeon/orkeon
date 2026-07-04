using Orkeon.Application.Memory.Queries.SearchMemory;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Service responsible for searching and filtering memory items within an agent memory store.
/// Encapsulates tier filtering, keyword matching, ordering by importance/recency, and result limiting.
/// </summary>
public interface IMemorySearchService
{
    /// <summary>
    /// Searches the given memory store for items matching the specified criteria.
    /// </summary>
    /// <param name="store">The agent memory store to search.</param>
    /// <param name="searchTerm">The keyword to match (case-insensitive).</param>
    /// <param name="memoryType">Optional memory tier filter (ShortTerm, LongTerm, or null for both).</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of matching memory items as DTOs, ordered by importance then recency.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItemDto>> SearchAsync(
        AgentMemoryStore store,
        string searchTerm,
        MemoryType? memoryType,
        int maxResults,
        CancellationToken cancellationToken = default);
}
