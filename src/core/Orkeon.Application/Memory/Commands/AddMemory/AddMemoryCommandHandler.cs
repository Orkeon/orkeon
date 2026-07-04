using Orkeon.Application.Common.CQRS;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Memory.Commands.AddMemory;

/// <summary>
/// Handler for adding a memory item to an agent's memory store.
/// </summary>
public partial class AddMemoryCommandHandler : ICommandHandler<AddMemoryCommand, AddMemoryResult>
{
    private readonly IAgentMemoryStoreRepository _memoryStoreRepository;
    private readonly ILogger<AddMemoryCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AddMemoryCommandHandler"/>.
    /// </summary>
    public AddMemoryCommandHandler(
        IAgentMemoryStoreRepository memoryStoreRepository,
        ILogger<AddMemoryCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(memoryStoreRepository);
        _memoryStoreRepository = memoryStoreRepository;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<AddMemoryResult> HandleAsync(
        AddMemoryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<AddMemoryResult> HandleCoreAsync()
        {
            LogAddingMemory(command.AgentId, command.MemoryType);

            var agentId = AgentId.Parse(command.AgentId);

            // Retrieve or create the memory store for this agent
            var store = await _memoryStoreRepository.GetByAgentIdAsync(agentId, cancellationToken).ConfigureAwait(false);
            if (store == null)
            {
                store = AgentMemoryStore.Create(agentId);
                await _memoryStoreRepository.AddAsync(store, cancellationToken).ConfigureAwait(false);
                LogCreatedMemoryStore(command.AgentId);
            }

            // Build the memory item
            var memoryItem = MemoryItem.Create(
                content: command.Content,
                importance: command.Importance,
                source: command.Source,
                tags: command.Tags,
                createdBy: agentId);

            var promotedToLongTerm = false;

            if (command.MemoryType == MemoryType.LongTerm)
            {
                store.PromoteToLongTermMemory(memoryItem);
                promotedToLongTerm = true;
            }
            else
            {
                // AddShortTermMemory may auto-promote based on importance
                var previousLongTermCount = store.LongTermMemory.Count;
                store.AddShortTermMemory(memoryItem);
                promotedToLongTerm = store.LongTermMemory.Count > previousLongTermCount;
            }

            await _memoryStoreRepository.UpdateAsync(store, cancellationToken).ConfigureAwait(false);

            LogMemoryAdded(command.AgentId, memoryItem.Id, promotedToLongTerm);

            return new AddMemoryResult(
                MemoryItemId: memoryItem.Id.ToString(),
                MemoryStoreId: store.Id.ToString(),
                PromotedToLongTerm: promotedToLongTerm);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding {MemoryType} memory for agent {AgentId}")]
    private partial void LogAddingMemory(string agentId, MemoryType memoryType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created new memory store for agent {AgentId}")]
    private partial void LogCreatedMemoryStore(string agentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Added memory item {MemoryItemId} for agent {AgentId} (promoted={PromotedToLongTerm})")]
    private partial void LogMemoryAdded(string agentId, object memoryItemId, bool promotedToLongTerm);
}
