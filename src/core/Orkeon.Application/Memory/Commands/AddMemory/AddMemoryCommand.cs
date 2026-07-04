using Orkeon.Application.Common.CQRS;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Memory.Commands.AddMemory;

/// <summary>
/// Command to add a memory item to an agent's memory store.
/// </summary>
public record AddMemoryCommand(
    string AgentId,
    string Content,
    MemoryType MemoryType,
    float Importance,
    string? Source,
    IReadOnlyList<string>? Tags
) : ICommand<AddMemoryResult>;

/// <summary>
/// Result returned after adding a memory item.
/// </summary>
public record AddMemoryResult(
    string MemoryItemId,
    string MemoryStoreId,
    bool PromotedToLongTerm
);
