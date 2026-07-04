using Microsoft.Extensions.Logging;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Task.DTOs;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Task.Queries.GetTask;

/// <summary>
/// Handler for getting a task by ID.
/// </summary>
public partial class GetTaskHandler : IQueryHandler<GetTaskQuery, TaskDto?>
{
    private readonly ITaskRepository _taskRepository;
    private readonly ILogger<GetTaskHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GetTaskHandler"/>.
    /// </summary>
    public GetTaskHandler(
        ITaskRepository taskRepository,
        ILogger<GetTaskHandler> logger)
    {
        _taskRepository = taskRepository;
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<TaskDto?> HandleAsync(
        GetTaskQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<TaskDto?> HandleCoreAsync()
        {
            LogGettingTask(query.Id);

            var taskId = TaskId.From(query.Id);
            var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);

            if (task == null)
            {
                LogTaskNotFound(query.Id);
                return null;
            }

            // Map to DTO and return
            return new TaskDto
            {
                Id = task.Id.ToString(),
                Name = task.Description.Value,
                Description = task.Description.Value,
                ExpectedOutput = task.ExpectedOutput.Value,
                AssignedAgent = task.AssignedAgent?.ToString(),
                Status = MapTaskStatus(task.Status),
                Priority = MapTaskPriority(task.Priority),
                CreatedAt = task.CreatedAt
            };
        }
    }

    private static string MapTaskStatus(Domain.Task.ValueObjects.TaskStatus domainStatus) =>
        domainStatus.Value switch
        {
            "Pending" => "Pending",
            "InProgress" => "InProgress",
            "Completed" => "Completed",
            "Failed" => "Failed",
            "Cancelled" => "Cancelled",
            "Blocked" => "Blocked",
            _ => "Pending"
        };

    private static string MapTaskPriority(Domain.Task.ValueObjects.TaskPriority domainPriority) =>
        domainPriority.Value switch
        {
            "Low" => "Low",
            "Normal" => "Normal",
            "High" => "High",
            "Critical" => "Critical",
            _ => "Normal"
        };

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting task with ID {Id}")]
    private partial void LogGettingTask(Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task with ID {Id} not found")]
    private partial void LogTaskNotFound(Guid id);
}
