using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Task.Commands.CreateTask;
using Orkeon.Application.Task.DTOs;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Answers a create-task command with a DTO built from it and records each command.</summary>
internal sealed class FakeCreateTaskCommandHandler : ICommandHandler<CreateTaskCommand, TaskDto>
{
    public List<CreateTaskCommand> Received { get; } = [];

    /// <summary>Agent written into the answered DTO, to exercise the assigned branches.</summary>
    public string? AssignedAgent { get; set; }

    public System.Threading.Tasks.Task<TaskDto> HandleAsync(CreateTaskCommand command, CancellationToken cancellationToken = default)
    {
        Received.Add(command);
        return System.Threading.Tasks.Task.FromResult(new TaskDto
        {
            Id = $"task-{Received.Count}",
            Name = $"task-{Received.Count}",
            Description = command.Description,
            ExpectedOutput = command.ExpectedOutput,
            AssignedAgent = AssignedAgent,
            Status = "Pending",
            Priority = "Medium",
        });
    }
}
