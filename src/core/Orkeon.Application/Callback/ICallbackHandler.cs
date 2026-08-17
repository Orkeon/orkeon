namespace Orkeon.Application.Callback;

/// <summary>
/// Comprehensive callback system for agents, tasks, and flow steps.
/// Covers the classic step_callback and task callback hooks.
/// </summary>
public interface ICallbackHandler
{
    /// <summary>
    /// Called before an agent step is executed.
    /// </summary>
    System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after an agent step is completed.
    /// </summary>
    System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before a task starts execution.
    /// </summary>
    System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called during task execution to report progress.
    /// </summary>
    System.Threading.Tasks.Task OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a task completes (success or failure).
    /// </summary>
    System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before a flow step starts execution.
    /// </summary>
    System.Threading.Tasks.Task OnFlowStepStartedAsync(FlowStepStartedContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a flow step completes.
    /// </summary>
    System.Threading.Tasks.Task OnFlowStepCompletedAsync(FlowStepCompletedContext context, CancellationToken cancellationToken = default);
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
/// Context provided during task execution progress.
/// </summary>
public record TaskProgressContext(
    string TaskId,
    string? AgentId,
    int StepNumber,
    int TotalSteps,
    double ProgressPercentage,
    string CurrentAction,
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

/// <summary>
/// Context provided when a flow step starts.
/// </summary>
public record FlowStepStartedContext(
    string FlowExecutionId,
    string FlowName,
    string StepId,
    string StepName,
    DateTime Timestamp);

/// <summary>
/// Groups the identity fields for a flow step.
/// </summary>
public record FlowStepIdentity(
    string FlowExecutionId,
    string FlowName,
    string StepId,
    string StepName);

/// <summary>
/// Context provided when a flow step completes.
/// </summary>
public record FlowStepCompletedContext(
    FlowStepIdentity FlowStep,
    bool Success,
    object? Output,
    string? Error,
    TimeSpan Duration,
    DateTime Timestamp)
{
    /// <summary>Convenience accessor for FlowExecutionId.</summary>
    public string FlowExecutionId => FlowStep.FlowExecutionId;
    /// <summary>Convenience accessor for FlowName.</summary>
    public string FlowName => FlowStep.FlowName;
    /// <summary>Convenience accessor for StepId.</summary>
    public string StepId => FlowStep.StepId;
    /// <summary>Convenience accessor for StepName.</summary>
    public string StepName => FlowStep.StepName;
}
