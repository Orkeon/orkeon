using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Execution;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Services.Generic;

/// <summary>
/// Generic interface for type-safe task execution.
/// Phase 3.1.2: Generic services with complete type safety.
/// </summary>
public interface ITaskExecutor<TTask, TResult>
    where TTask : ICrewTask
    where TResult : class
{
    /// <summary>Execute Async(T Task, I Execution Context, Cancellation Token).</summary>
    System.Threading.Tasks.Task<TResult> ExecuteAsync(
        TTask task,
        IExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstract base class for generic task executors.
/// Provides common validation and execution patterns.
/// Subclasses specify TContext to get strongly-typed data access via GetTypedContext.
/// </summary>
public abstract partial class TaskExecutorBase<TTask, TResult>
    : ITaskExecutor<TTask, TResult>
    where TTask : ICrewTask
    where TResult : class
{
    /// <summary>Logger.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "The [LoggerMessage] source generator resolves the ILogger via an instance field; converting it to a property breaks generation of the partial log methods in this type and its subclasses.")]
    protected readonly ILogger Logger;

    /// <summary>Task Executor Base(I Logger).</summary>
    protected TaskExecutorBase(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Logger = logger;
    }

    /// <summary>
    /// Main execution method with validation and error handling.
    /// </summary>
    public System.Threading.Tasks.Task<TResult> ExecuteAsync(
        TTask task,
        IExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteInternalAsync(task, context, cancellationToken);
    }

    /// <summary>
    /// Helper to retrieve a strongly-typed execution context from the non-generic interface.
    /// </summary>
    protected static ExecutionContext<TContext> GetTypedContext<TContext>(IExecutionContext context)
        where TContext : class, new()
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context is ExecutionContext<TContext> typed)
            return typed;
        throw new InvalidOperationException(
            $"Expected ExecutionContext<{typeof(TContext).Name}> but received {context.GetType().Name}.");
    }

    private async System.Threading.Tasks.Task<TResult> ExecuteInternalAsync(
        TTask task,
        IExecutionContext context,
        CancellationToken cancellationToken)
    {
        LogExecutingTask(typeof(TTask).Name, task.TaskId);

        // Pre-execution validation
        var validation = await ValidateAsync(task, context).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            LogTaskValidationFailed(task.TaskId, string.Join(", ", validation.Errors));
            throw new TaskValidationException(validation.Errors);
        }

        try
        {
            // Core execution
            var result = await ExecuteCoreAsync(task, context, cancellationToken).ConfigureAwait(false);

            // Post-processing
            await PostProcessAsync(task, context, result).ConfigureAwait(false);

            LogTaskExecutedSuccessfully(typeof(TTask).Name, task.TaskId);

            return result;
        }
        catch (OperationCanceledException)
        {
            LogTaskExecutionCancelled(task.TaskId);
            throw;
        }
        catch (Exception ex)
        {
            LogTaskExecutionError(ex, typeof(TTask).Name, task.TaskId);
            throw;
        }
    }

    /// <summary>
    /// Validates the task and context before execution.
    /// Override to provide task-specific validation logic.
    /// </summary>
    protected virtual System.Threading.Tasks.Task<ValidationResult> ValidateAsync(
        TTask task,
        IExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<string>();

        // Basic validation
        if (task.TaskId == null || string.IsNullOrWhiteSpace(task.TaskId.Value.ToString()))
            errors.Add("Task ID is required");

        if (string.IsNullOrWhiteSpace(task.Description?.Value))
            errors.Add("Task description is required");

        if (context.IsCancelled)
            errors.Add("Execution context is already cancelled");

        return System.Threading.Tasks.Task.FromResult(new ValidationResult(errors));
    }

    /// <summary>
    /// Core execution logic. Must be implemented by derived classes.
    /// </summary>
    protected abstract System.Threading.Tasks.Task<TResult> ExecuteCoreAsync(
        TTask task,
        IExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Post-processing logic after successful execution.
    /// Override to provide cleanup or additional processing.
    /// </summary>
    protected virtual System.Threading.Tasks.Task PostProcessAsync(
        TTask task,
        IExecutionContext context,
        TResult result)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Executing {TaskType} with ID {TaskId}")]
    private partial void LogExecutingTask(string taskType, object? taskId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Task validation failed for {TaskId}: {Errors}")]
    private partial void LogTaskValidationFailed(object? taskId, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully executed {TaskType} with ID {TaskId}")]
    private partial void LogTaskExecutedSuccessfully(string taskType, object? taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task execution cancelled for {TaskId}")]
    private partial void LogTaskExecutionCancelled(object? taskId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing {TaskType} with ID {TaskId}")]
    private partial void LogTaskExecutionError(Exception ex, string taskType, object? taskId);
}

/// <summary>
/// Validation result for task execution.
/// </summary>
public record ValidationResult(IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Gets or sets a value indicating whether is valid.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Success.
    /// </summary>
    public static ValidationResult Success() => new(Array.Empty<string>());
    /// <summary>
    /// Failed.
    /// </summary>
    public static ValidationResult Failed(params string[] errors) => new(errors);
    /// <summary>
    /// Failed.
    /// </summary>
    public static ValidationResult Failed(IEnumerable<string> errors) => new(errors.ToList());
}

/// <summary>
/// Exception thrown when task validation fails.
/// </summary>
[Serializable]
public class TaskValidationException : Exception
{
    /// <summary>Gets or sets the validation errors.</summary>
    public IReadOnlyList<string> ValidationErrors { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="TaskValidationException"/>.
    /// </summary>
    public TaskValidationException() : base("Task validation failed.")
    {
        ValidationErrors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="TaskValidationException"/>.
    /// </summary>
    public TaskValidationException(IReadOnlyList<string> errors)
        : base($"Task validation failed: {string.Join(", ", errors)}")
    {
        ValidationErrors = errors;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="TaskValidationException"/>.
    /// </summary>
    public TaskValidationException(string error)
        : this([error])
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="TaskValidationException"/> with an inner exception.
    /// </summary>
    public TaskValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        ValidationErrors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="TaskValidationException"/> for deserialization.
    /// </summary>
#pragma warning disable CA2229, SYSLIB0051 // Required by S3925 ISerializable pattern
    protected TaskValidationException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ValidationErrors = Array.Empty<string>();
    }
#pragma warning restore CA2229, SYSLIB0051
}

/// <summary>
/// Factory for creating task executors based on task type.
/// Provides type-safe task executor resolution.
/// </summary>
public interface ITaskExecutorFactory
{
    /// <summary>Gets the executor for the specified task and result types.</summary>
    ITaskExecutor<TTask, TResult>? GetExecutor<TTask, TResult>()
        where TTask : ICrewTask
        where TResult : class;

    /// <summary>Determines whether this factory can execute the specified task type.</summary>
    bool CanExecute<TTask>() where TTask : ICrewTask;
}

/// <summary>
/// Default implementation of task executor factory.
/// </summary>
public partial class TaskExecutorFactory : ITaskExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskExecutorFactory> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="TaskExecutorFactory"/>.
    /// </summary>
    public TaskExecutorFactory(
        IServiceProvider serviceProvider,
        ILogger<TaskExecutorFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Gets the executor for the specified task and result types.</summary>
    public ITaskExecutor<TTask, TResult>? GetExecutor<TTask, TResult>()
        where TTask : ICrewTask
        where TResult : class
    {
        try
        {
            var executor = _serviceProvider.GetService(typeof(ITaskExecutor<TTask, TResult>)) as ITaskExecutor<TTask, TResult>;
            if (executor == null)
            {
                LogNoExecutorFound(typeof(TTask).Name);
                return null;
            }

            return executor;
        }
        catch (Exception ex)
        {
            LogExecutorCreationError(ex, typeof(TTask).Name);
            throw;
        }
    }

    /// <summary>
    /// Can Execute.
    /// </summary>
    public bool CanExecute<TTask>() where TTask : ICrewTask
    {
        try
        {
            var executorType = typeof(ITaskExecutor<,>).MakeGenericType(
                typeof(TTask),
                typeof(object)  // Default result type
            );

            return _serviceProvider.GetService(executorType) != null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No executor found for task type {TaskType}")]
    private partial void LogNoExecutorFound(string taskType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating executor for task type {TaskType}")]
    private partial void LogExecutorCreationError(Exception ex, string taskType);

}
