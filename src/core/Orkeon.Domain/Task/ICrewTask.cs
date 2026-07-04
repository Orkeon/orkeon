using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

using Orkeon.Domain.SharedKernel;

namespace Orkeon.Domain.Task;

/// <summary>
/// Interface for task implementations.
/// </summary>
public interface ICrewTask
{
    /// <summary>Gets the task identifier.</summary>
    TaskId TaskId { get; }
    /// <summary>Gets the task description.</summary>
    TaskDescription Description { get; }
    /// <summary>Gets the expected output description.</summary>
    ExpectedOutput ExpectedOutput { get; }
    /// <summary>Gets the identifier of the assigned agent, or <see langword="null"/> if unassigned.</summary>
    AgentId? AssignedAgent { get; }
    /// <summary>Gets the current task status.</summary>
    TaskStatus Status { get; }
    /// <summary>Gets the task output, or <see langword="null"/> if not yet completed.</summary>
    TaskOutput? Output { get; }
    /// <summary>Gets the list of task dependencies.</summary>
    IReadOnlyList<TaskId> Dependencies { get; }
    /// <summary>Gets when the task was created.</summary>
    DateTime CreatedAt { get; }
    /// <summary>Gets when the task was started, or <see langword="null"/> if not started.</summary>
    DateTime? StartedAt { get; }
    /// <summary>Gets when the task was completed, or <see langword="null"/> if not completed.</summary>
    DateTime? CompletedAt { get; }
    /// <summary>Gets whether this task executes asynchronously.</summary>
    bool AsyncExecution { get; }
    /// <summary>Gets the JSON schema for the output, or <see langword="null"/> if not specified.</summary>
    JsonSchema? OutputJson { get; }
    /// <summary>Gets the Pydantic model type for the output, or <see langword="null"/> if not specified.</summary>
    Type? OutputPydantic { get; }
    /// <summary>Gets the output file path, or <see langword="null"/> if not specified.</summary>
    string? OutputFile { get; }
    /// <summary>Gets whether this task requires human input.</summary>
    bool HumanInput { get; }

    /// <summary>Assigns this task to an agent.</summary>
    /// <param name="agentId">The agent identifier to assign to.</param>
    void AssignTo(AgentId agentId);
    /// <summary>Starts execution of this task.</summary>
    /// <param name="agentId">The agent identifier starting the task.</param>
    void Start(AgentId agentId);
    /// <summary>Marks this task as completed.</summary>
    /// <param name="agentId">The agent identifier that completed the task.</param>
    /// <param name="output">The task output.</param>
    void Complete(AgentId agentId, TaskOutput output);
    /// <summary>Marks this task as failed.</summary>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="exception">The optional exception that caused the failure.</param>
    void Fail(string errorMessage, Exception? exception = null);
    /// <summary>Determines whether this task can execute given completed tasks.</summary>
    /// <param name="isTaskCompleted">A function that returns whether a given task is completed.</param>
    /// <returns><see langword="true"/> if the task can execute; otherwise <see langword="false"/>.</returns>
    bool CanExecute(Func<TaskId, bool> isTaskCompleted);
    /// <summary>Validates the task output.</summary>
    /// <param name="output">The output to validate.</param>
    /// <returns>The validation result.</returns>
    ValidationResult ValidateOutput(TaskOutput output);
    /// <summary>Gets a summary of the task context.</summary>
    /// <returns>A context summary string.</returns>
    string GetContextSummary();
    /// <summary>Gets the total execution time of this task.</summary>
    /// <returns>The execution time, or zero if not yet completed.</returns>
    TimeSpan GetExecutionTime();
}
