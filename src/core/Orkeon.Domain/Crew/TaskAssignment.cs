using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew;

/// <summary>Represents the assignment of a task to an agent.</summary>
/// <param name="TaskId">The identifier of the assigned task.</param>
/// <param name="AssignedAgent">The identifier of the agent the task was assigned to.</param>
/// <param name="Reason">The reason for this assignment.</param>
/// <param name="AssignedAt">The timestamp when the assignment was made.</param>
public record TaskAssignment(
    TaskId TaskId,
    AgentId AssignedAgent,
    string Reason,
    DateTime AssignedAt
);
