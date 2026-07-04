using Orkeon.Application.Common.CQRS;

namespace Orkeon.Application.Memory.Commands.CreateMemoryStore;

/// <summary>
/// Command to create a new memory store for an agent.
/// </summary>
public record CreateMemoryStoreCommand(
    string AgentId,
    int ShortTermCapacity,
    int LongTermCapacity
) : ICommand<CreateMemoryStoreResult>;

/// <summary>
/// Result returned after creating a memory store.
/// </summary>
public record CreateMemoryStoreResult(
    string MemoryStoreId,
    string AgentId,
    int ShortTermCapacity,
    int LongTermCapacity
);
