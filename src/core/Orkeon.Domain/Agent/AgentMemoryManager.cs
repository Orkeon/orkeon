using Orkeon.Domain.Constants.Agent;
namespace Orkeon.Domain.Agent;

/// <summary>
/// Manages memory entries for an Agent: add, trim, and query operations.
/// Extracted from Agent to follow Single Responsibility Principle.
/// This is a domain helper (POCO), not a service — no dependency injection.
/// </summary>
internal sealed class AgentMemoryManager
{
    private readonly List<AgentMemory> _memories;
    public AgentMemoryManager(List<AgentMemory> memories)
    {
        ArgumentNullException.ThrowIfNull(memories);
        _memories = memories;
    }

    /// <summary>
    /// Gets the memories as a read-only list.
    /// </summary>
    public IReadOnlyList<AgentMemory> Memories => _memories.AsReadOnly();

    /// <summary>
    /// Adds a memory and trims old entries if the limit is exceeded.
    /// </summary>
    /// <returns>The added memory (for event raising).</returns>
    public AgentMemory AddMemory(AgentMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        _memories.Add(memory);

        // Keep only the most recent memories
        if (_memories.Count > AgentDefaults.MaxMemories)
        {
            _memories.RemoveRange(0, _memories.Count - AgentDefaults.MaxMemories);
        }

        return memory;
    }
}
