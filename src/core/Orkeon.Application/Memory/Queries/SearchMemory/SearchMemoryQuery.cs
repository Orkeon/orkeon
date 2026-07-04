using Orkeon.Application.Common.CQRS;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Memory.Queries.SearchMemory;

/// <summary>
/// Query to search memory items for a given agent.
/// </summary>
public record SearchMemoryQuery(
    string AgentId,
    string SearchTerm,
    MemoryType? MemoryType,
    int MaxResults
) : IQuery<SearchMemoryResult>;

/// <summary>
/// A single memory item returned by a search.
/// </summary>
public record MemoryItemDto(
    string Id,
    string Content,
    float Importance,
    string Source,
    IReadOnlyList<string> Tags,
    DateTime Timestamp,
    bool IsLongTerm
);

/// <summary>
/// Result returned by the search memory query.
/// </summary>
public record SearchMemoryResult(
    IReadOnlyList<MemoryItemDto> Items,
    int TotalCount
);
