namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>Result of a process execution.</summary>
public record ProcessResult
{
    /// <summary>Gets a value indicating whether the process succeeded.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets the error message if the process failed, otherwise null.</summary>
    public string? ErrorMessage { get; }
    /// <summary>Gets the outputs produced by the process.</summary>
    public ProcessOutputs Outputs { get; }
    /// <summary>Gets the total execution time of the process.</summary>
    public TimeSpan ExecutionTime { get; }
    /// <summary>Gets the timestamp when the process started.</summary>
    public DateTime StartedAt { get; }
    /// <summary>Gets the timestamp when the process completed, or null if not yet completed.</summary>
    public DateTime? CompletedAt { get; }

    /// <summary>Initializes a new instance of <see cref="ProcessResult"/>.</summary>
    /// <param name="isSuccess">Whether the process succeeded.</param>
    /// <param name="outputs">The process outputs.</param>
    /// <param name="errorMessage">The error message on failure.</param>
    /// <param name="executionTime">The total execution time.</param>
    /// <param name="startedAt">The start timestamp.</param>
    /// <param name="completedAt">The completion timestamp.</param>
    private ProcessResult(
        bool isSuccess,
        ProcessOutputs? outputs = null,
        string? errorMessage = null,
        TimeSpan executionTime = default,
        DateTime startedAt = default,
        DateTime? completedAt = null)
    {
        if (executionTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(executionTime), "Execution time cannot be negative.");

        if (!isSuccess && string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("Error message is required when the process has failed.", nameof(errorMessage));

        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Outputs = outputs ?? ProcessOutputs.Empty;
        ExecutionTime = executionTime;
        StartedAt = startedAt == default ? DateTime.UtcNow : startedAt;
        CompletedAt = completedAt;
    }

    /// <summary>Creates a new <see cref="ProcessResult"/> instance.</summary>
    public static ProcessResult Create(
        bool isSuccess,
        ProcessOutputs? outputs = null,
        string? errorMessage = null,
        TimeSpan executionTime = default,
        DateTime startedAt = default,
        DateTime? completedAt = null)
        => new(isSuccess, outputs, errorMessage, executionTime, startedAt, completedAt);

    /// <summary>Creates a successful process result with optional outputs.</summary>
    /// <param name="outputs">The process outputs.</param>
    /// <returns>A successful <see cref="ProcessResult"/>.</returns>
    public static ProcessResult Success(ProcessOutputs? outputs = null)
    {
        return new ProcessResult(true, outputs);
    }

    /// <summary>Creates a successful process result with a single key-value output.</summary>
    /// <param name="key">The output key.</param>
    /// <param name="value">The output value.</param>
    /// <returns>A successful <see cref="ProcessResult"/>.</returns>
    public static ProcessResult Success(string key, object value)
    {
        var outputs = ProcessOutputs.Empty.With(key, value);
        return new ProcessResult(true, outputs);
    }

    /// <summary>Creates a failed process result with an error message.</summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A failed <see cref="ProcessResult"/>.</returns>
    public static ProcessResult Failure(string errorMessage)
    {
        return new ProcessResult(false, errorMessage: errorMessage);
    }
}

/// <summary>Groups execution timing information.</summary>
/// <param name="TotalExecutionTime">The total execution time.</param>
/// <param name="StartedAt">The start timestamp.</param>
/// <param name="CompletedAt">The completion timestamp, or null.</param>
public record ExecutionTiming(
    TimeSpan TotalExecutionTime = default,
    DateTime StartedAt = default,
    DateTime? CompletedAt = null)
{
    /// <summary>Default timing starting at current UTC time.</summary>
    public static readonly ExecutionTiming Default = new();
}

/// <summary>Groups task completion lists for crew results.</summary>
/// <param name="CompletedTasks">The list of completed task identifiers.</param>
/// <param name="FailedTasks">The list of failed task identifiers.</param>
public record TaskCompletionLists(
    IReadOnlyList<string>? CompletedTasks = null,
    IReadOnlyList<string>? FailedTasks = null);

/// <summary>Result of a crew execution.</summary>
public record CrewResult
{
    /// <summary>Gets a value indicating whether the crew execution succeeded.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets the error message if the execution failed, otherwise null.</summary>
    public string? ErrorMessage { get; }
    /// <summary>Gets the outputs from all tasks in the crew.</summary>
    public TaskOutputMap TaskOutputs { get; }
    /// <summary>Gets the list of completed task identifiers.</summary>
    public IReadOnlyList<string> CompletedTasks { get; }
    /// <summary>Gets the list of failed task identifiers.</summary>
    public IReadOnlyList<string> FailedTasks { get; }
    /// <summary>Gets the total execution time across all tasks.</summary>
    public TimeSpan TotalExecutionTime { get; }
    /// <summary>Gets the timestamp when execution started.</summary>
    public DateTime StartedAt { get; }
    /// <summary>Gets the timestamp when execution completed, or null if not yet completed.</summary>
    public DateTime? CompletedAt { get; }

    /// <summary>Initializes a new instance of <see cref="CrewResult"/>.</summary>
    /// <param name="isSuccess">Whether the execution succeeded.</param>
    /// <param name="taskOutputs">The task outputs.</param>
    /// <param name="errorMessage">The error message on failure.</param>
    /// <param name="taskLists">Task completion lists.</param>
    /// <param name="timing">Execution timing information.</param>
    private CrewResult(
        bool isSuccess,
        TaskOutputMap? taskOutputs = null,
        string? errorMessage = null,
        TaskCompletionLists? taskLists = null,
        ExecutionTiming? timing = null)
    {
        var t = timing ?? ExecutionTiming.Default;

        if (t.TotalExecutionTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timing), "Total execution time cannot be negative.");

        if (!isSuccess && string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("Error message is required when the crew execution has failed.", nameof(errorMessage));

        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        TaskOutputs = taskOutputs ?? TaskOutputMap.Empty;
        var lists = taskLists ?? new TaskCompletionLists();
        CompletedTasks = lists.CompletedTasks ?? Array.Empty<string>();
        FailedTasks = lists.FailedTasks ?? Array.Empty<string>();
        TotalExecutionTime = t.TotalExecutionTime;
        StartedAt = t.StartedAt == default ? DateTime.UtcNow : t.StartedAt;
        CompletedAt = t.CompletedAt;
    }

    /// <summary>Creates a new <see cref="CrewResult"/> instance.</summary>
    public static CrewResult Create(
        bool isSuccess,
        TaskOutputMap? taskOutputs = null,
        string? errorMessage = null,
        TaskCompletionLists? taskLists = null,
        ExecutionTiming? timing = null)
        => new(isSuccess, taskOutputs, errorMessage, taskLists, timing);

    /// <summary>Creates a successful crew result.</summary>
    /// <param name="outputs">The task outputs.</param>
    /// <param name="completedTasks">The completed task identifiers.</param>
    /// <returns>A successful <see cref="CrewResult"/>.</returns>
    public static CrewResult Success(TaskOutputMap? outputs = null, IReadOnlyList<string>? completedTasks = null)
    {
        return new CrewResult(true, outputs, taskLists: new TaskCompletionLists(completedTasks));
    }

    /// <summary>Creates a failed crew result.</summary>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="failedTasks">The failed task identifiers.</param>
    /// <returns>A failed <see cref="CrewResult"/>.</returns>
    public static CrewResult Failure(string errorMessage, IReadOnlyList<string>? failedTasks = null)
    {
        return new CrewResult(false, errorMessage: errorMessage, taskLists: new TaskCompletionLists(FailedTasks: failedTasks));
    }
}
