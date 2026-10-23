namespace Orkeon.Infrastructure.Session;

/// <summary>Configuration of the durable agent memory.</summary>
public sealed record ScriptAgentMemoryOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Orkeon:Scripting:AgentMemory";

    /// <summary>Root directory of the memory files.</summary>
    public string RootDirectory { get; init; } = "/workspace/.orkeon/agent-memory";
}
