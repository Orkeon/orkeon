using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Task.DTOs;

namespace Orkeon.Application.Task.Commands.CreateTask;

/// <summary>
/// Command to create a new task.
/// </summary>
public record CreateTaskCommand(
    string Description,
    string ExpectedOutput,
    string? AgentId
) : ICommand<TaskDto>;
