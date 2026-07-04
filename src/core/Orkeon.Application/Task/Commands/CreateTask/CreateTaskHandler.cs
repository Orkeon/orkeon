using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Task.DTOs;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Task.Commands.CreateTask;

/// <summary>
/// Handler for creating a new task.
/// </summary>
public partial class CreateTaskHandler : ICommandHandler<CreateTaskCommand, TaskDto>
{
    private readonly ITaskRepository _taskRepository;
    private readonly ILogger<CreateTaskHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CreateTaskHandler"/>.
    /// </summary>
    public CreateTaskHandler(
        ITaskRepository taskRepository,
        ILogger<CreateTaskHandler> logger)
    {
        _taskRepository = taskRepository;
        _logger = logger;
    }

    /// <summary>
    /// Handle Async.
    /// </summary>
    public System.Threading.Tasks.Task<TaskDto> HandleAsync(
        CreateTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return HandleCoreAsync();

        async System.Threading.Tasks.Task<TaskDto> HandleCoreAsync()
        {
            LogCreatingTask(command.Description);

            // Create value objects
            var description = TaskDescription.From(command.Description);
            var expectedOutput = ExpectedOutput.From(command.ExpectedOutput);

            // Create the task domain entity
            var task = CrewTask.Create(
                description: description,
                expectedOutput: expectedOutput);

            // Assign agent if provided (format already validated by CreateTaskCommandValidator)
            if (!string.IsNullOrWhiteSpace(command.AgentId))
            {
                var agentId = AgentId.From(Guid.Parse(command.AgentId));
                task.AssignTo(agentId);
                LogAgentAssigned(command.AgentId);
            }

            // Save the task
            await _taskRepository.AddAsync(task, cancellationToken).ConfigureAwait(false);

            // Map to DTO and return
            return new TaskDto
            {
                Id = task.Id.ToString(),
                Name = task.Description.Value,
                Description = task.Description.Value,
                ExpectedOutput = task.ExpectedOutput.Value,
                AssignedAgent = task.AssignedAgent?.ToString(),
                Status = "Pending",
                Priority = "Normal",
                CreatedAt = task.CreatedAt
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating task with description {Description}")]
    private partial void LogCreatingTask(string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} assigned to task")]
    private partial void LogAgentAssigned(string agentId);
}
