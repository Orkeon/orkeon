namespace Orkeon.Domain.Task;

/// <summary>
/// Fine-grained execution states for a task being processed by an agent.
/// These are runtime states used by the FSM during execution, complementing
/// the persisted <see cref="ValueObjects.TaskStatus"/>.
/// </summary>
public enum TaskExecutionState
{
    /// <summary>Task assigned to agent, waiting to start.</summary>
    Assigned,

    /// <summary>Agent is planning how to approach the task.</summary>
    Planning,

    /// <summary>Agent is executing (LLM inference in progress).</summary>
    Executing,

    /// <summary>Agent is calling a tool and waiting for results.</summary>
    ToolCalling,

    /// <summary>Agent is validating the output against expected criteria.</summary>
    Validating,

    /// <summary>Task is waiting for human input/review.</summary>
    WaitingForHumanInput,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task failed and is eligible for retry.</summary>
    Failed,

    /// <summary>Task has been cancelled.</summary>
    Cancelled,

    /// <summary>
    /// Degraded mode: circuit breaker tripped, task is in a safe
    /// fallback state with partial output preserved.
    /// </summary>
    Degraded
}

/// <summary>
/// Events that drive transitions in the task execution FSM.
/// </summary>
public enum TaskExecutionEvent
{
    /// <summary>Agent starts planning the approach.</summary>
    StartPlanning,

    /// <summary>Planning complete, begin execution.</summary>
    BeginExecution,

    /// <summary>LLM requests a tool call.</summary>
    RequestToolCall,

    /// <summary>Tool call completed, resume execution.</summary>
    ToolCallCompleted,

    /// <summary>Tool call failed.</summary>
    ToolCallFailed,

    /// <summary>Execution round complete, move to validation.</summary>
    SubmitForValidation,

    /// <summary>Validation passed.</summary>
    ValidationPassed,

    /// <summary>Validation failed, retry execution.</summary>
    ValidationFailed,

    /// <summary>Human input is required.</summary>
    RequestHumanInput,

    /// <summary>Human input received, resume execution.</summary>
    HumanInputReceived,

    /// <summary>Unrecoverable failure.</summary>
    Fail,

    /// <summary>Retry after failure.</summary>
    Retry,

    /// <summary>Cancel the task.</summary>
    Cancel
}
