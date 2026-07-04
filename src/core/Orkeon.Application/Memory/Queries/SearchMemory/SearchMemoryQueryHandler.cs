using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Memory.Queries.SearchMemory;

/// <summary>
/// Handler for searching memory items belonging to an agent.
/// </summary>
public partial class SearchMemoryQueryHandler : IQueryHandler<SearchMemoryQuery, SearchMemoryResult>
{
    private readonly IAgentMemoryStoreRepository _memoryStoreRepository;
    private readonly IMemorySearchService _memorySearchService;
    private readonly ILogger<SearchMemoryQueryHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SearchMemoryQueryHandler"/>.
    /// </summary>
    public SearchMemoryQueryHandler(
        IAgentMemoryStoreRepository memoryStoreRepository,
        IMemorySearchService memorySearchService,
        ILogger<SearchMemoryQueryHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(memoryStoreRepository);
        _memoryStoreRepository = memoryStoreRepository;
        ArgumentNullException.ThrowIfNull(memorySearchService);
        _memorySearchService = memorySearchService;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<SearchMemoryResult> HandleAsync(
        SearchMemoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<SearchMemoryResult> HandleCoreAsync()
        {
            LogSearchingMemory(query.AgentId, query.SearchTerm);

            var agentId = AgentId.Parse(query.AgentId);
            var store = await _memoryStoreRepository.GetByAgentIdAsync(agentId, cancellationToken).ConfigureAwait(false);

            if (store == null)
            {
                LogMemoryStoreNotFound(query.AgentId);
                return new SearchMemoryResult(Items: [], TotalCount: 0);
            }

            var results = await _memorySearchService.SearchAsync(
                store,
                query.SearchTerm,
                query.MemoryType,
                query.MaxResults,
                cancellationToken).ConfigureAwait(false);

            LogSearchCompleted(query.AgentId, results.Count);

            return new SearchMemoryResult(Items: results, TotalCount: results.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Searching memory for agent {AgentId} with term '{SearchTerm}'")]
    private partial void LogSearchingMemory(string agentId, string searchTerm);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory store not found for agent {AgentId}")]
    private partial void LogMemoryStoreNotFound(string agentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Memory search for agent {AgentId} returned {Count} results")]
    private partial void LogSearchCompleted(string agentId, int count);
}
