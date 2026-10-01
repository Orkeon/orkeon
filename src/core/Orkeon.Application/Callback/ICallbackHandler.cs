namespace Orkeon.Application.Callback;

/// <summary>
/// Task- and step-level notifications of a crew run. Every registered handler is called by
/// <see cref="Interfaces.Services.ICallbackOrchestrator"/>: task start and end around each
/// agent execution, and a step — started, then completed — around each tool call of the agent
/// loops. A handler that throws is logged and skipped; it never changes the run.
/// </summary>
public interface ICallbackHandler
{
    /// <summary>
    /// Called before an agent step — one tool call — is executed.
    /// </summary>
    System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after an agent step — one tool call — completed, failed or was blocked.
    /// </summary>
    System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before a task starts execution.
    /// </summary>
    System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default);


    /// <summary>
    /// Called after a task completes (success or failure).
    /// </summary>
    System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default);


}

/// <summary>
/// Context provided when an agent step starts.
/// </summary>
public record StepStartedContext(
    string AgentId,
    string AgentRole,
    string TaskId,
    string Action,
    string Thought,
    DateTime Timestamp);

/// <summary>
/// Groups the identity fields for a step (agent + task).
/// </summary>
public record StepIdentity(
    string AgentId,
    string AgentRole,
    string TaskId);

/// <summary>
/// Context provided when an agent step completes.
/// </summary>
public record StepCompletedContext(
    StepIdentity Step,
    string Action,
    string Thought,
    string Observation,
    bool Success,
    TimeSpan Duration,
    DateTime Timestamp)
{
    /// <summary>Convenience accessor for AgentId.</summary>
    public string AgentId => Step.AgentId;
    /// <summary>Convenience accessor for AgentRole.</summary>
    public string AgentRole => Step.AgentRole;
    /// <summary>Convenience accessor for TaskId.</summary>
    public string TaskId => Step.TaskId;
}

/// <summary>
/// Context provided when a task starts.
/// </summary>
public record TaskStartedContext(
    string TaskId,
    string Description,
    string ExpectedOutput,
    string? AgentId,
    string? AgentRole,
    DateTime Timestamp);

/// <summary>
/// Groups the outcome fields for a completed task.
/// </summary>
public record TaskExecutionOutcome(
    bool Success,
    string? Output,
    object? StructuredOutput,
    string? Error);

/// <summary>
/// Context provided when a task completes.
/// </summary>
public record TaskCompletedContext(
    string TaskId,
    string? AgentId,
    TaskExecutionOutcome Outcome,
    TimeSpan Duration,
    int StepsExecuted,
    DateTime Timestamp)
{
    /// <summary>Convenience accessor for Success.</summary>
    public bool Success => Outcome.Success;
    /// <summary>Convenience accessor for Output.</summary>
    public string? Output => Outcome.Output;
    /// <summary>Convenience accessor for StructuredOutput.</summary>
    public object? StructuredOutput => Outcome.StructuredOutput;
    /// <summary>Convenience accessor for Error.</summary>
    public string? Error => Outcome.Error;
}
