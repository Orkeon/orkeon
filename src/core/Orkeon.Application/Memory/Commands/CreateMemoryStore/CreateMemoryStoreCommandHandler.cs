using Orkeon.Application.Common.CQRS;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Memory.Commands.CreateMemoryStore;

/// <summary>
/// Handler for creating a new memory store for an agent.
/// </summary>
public partial class CreateMemoryStoreCommandHandler : ICommandHandler<CreateMemoryStoreCommand, CreateMemoryStoreResult>
{
    private readonly IAgentMemoryStoreRepository _memoryStoreRepository;
    private readonly ILogger<CreateMemoryStoreCommandHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CreateMemoryStoreCommandHandler"/>.
    /// </summary>
    public CreateMemoryStoreCommandHandler(
        IAgentMemoryStoreRepository memoryStoreRepository,
        ILogger<CreateMemoryStoreCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(memoryStoreRepository);
        _memoryStoreRepository = memoryStoreRepository;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<CreateMemoryStoreResult> HandleAsync(
        CreateMemoryStoreCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<CreateMemoryStoreResult> HandleCoreAsync()
        {
            LogCreatingMemoryStore(command.AgentId);

            var agentId = AgentId.Parse(command.AgentId);

            // Check if a memory store already exists for this agent
            var existing = await _memoryStoreRepository.GetByAgentIdAsync(agentId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                LogMemoryStoreAlreadyExists(command.AgentId, existing.Id);

                return new CreateMemoryStoreResult(
                    MemoryStoreId: existing.Id.ToString(),
                    AgentId: command.AgentId,
                    ShortTermCapacity: existing.ShortTermCapacity,
                    LongTermCapacity: existing.LongTermCapacity);
            }

            // Create the domain aggregate
            var store = AgentMemoryStore.Create(
                ownerAgentId: agentId,
                shortTermCapacity: command.ShortTermCapacity,
                longTermCapacity: command.LongTermCapacity);

            await _memoryStoreRepository.AddAsync(store, cancellationToken).ConfigureAwait(false);

            LogMemoryStoreCreated(command.AgentId, store.Id);

            return new CreateMemoryStoreResult(
                MemoryStoreId: store.Id.ToString(),
                AgentId: command.AgentId,
                ShortTermCapacity: store.ShortTermCapacity,
                LongTermCapacity: store.LongTermCapacity);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating memory store for agent {AgentId}")]
    private partial void LogCreatingMemoryStore(string agentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory store already exists for agent {AgentId}: {MemoryStoreId}")]
    private partial void LogMemoryStoreAlreadyExists(string agentId, object memoryStoreId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created memory store {MemoryStoreId} for agent {AgentId}")]
    private partial void LogMemoryStoreCreated(string agentId, object memoryStoreId);
}
