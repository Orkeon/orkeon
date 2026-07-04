namespace Orkeon.Domain.Agent;

/// <summary>
/// Context for step progress notifications during task execution.
/// </summary>
public record StepProgressContext(
    string TaskId,
    string AgentId,
    string StepDescription,
    int CurrentStep,
    int TotalSteps,
    DateTime Timestamp);
