namespace Orkeon.Application.Memory;

/// <summary>
/// One memory of a crew, recalled into the prompt of a task (GAP-30): when it was stored, the role
/// of the agent that produced it, the task it answered, and its content — cut, for the last one, to
/// what remains of the recall's character budget (<see cref="CrewMemoryOptions.MaxChars"/>).
/// </summary>
/// <param name="StoredAt">When the memory was stored (UTC).</param>
/// <param name="AgentRole">The role of the agent whose output it is; empty when unknown.</param>
/// <param name="TaskDescription">The task it answered; empty when unknown.</param>
/// <param name="Content">What was remembered.</param>
public sealed record RecalledMemory(DateTime StoredAt, string AgentRole, string TaskDescription, string Content);
