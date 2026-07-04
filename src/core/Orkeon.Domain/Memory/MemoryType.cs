namespace Orkeon.Domain.Memory;

/// <summary>
/// Represents the type of memory storage.
/// </summary>
public enum MemoryType
{
    /// <summary>
    /// Short-term memory for recent events and immediate context.
    /// </summary>
    ShortTerm,

    /// <summary>
    /// Long-term memory for important persistent information.
    /// </summary>
    LongTerm,

    /// <summary>
    /// Episodic memory for complete event sequences.
    /// </summary>
    Episodic,

    /// <summary>
    /// Entity memory for information about specific entities.
    /// </summary>
    Entity,

    /// <summary>
    /// Procedural memory for learned skills and procedures.
    /// </summary>
    Procedural
}
