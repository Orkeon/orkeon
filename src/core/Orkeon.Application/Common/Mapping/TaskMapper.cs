using System.Collections.Immutable;
using Orkeon.Application.Task.DTOs;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Common.Mapping;

/// <summary>
/// Simplified mapper for converting between Task domain entities and DTOs.
/// Uses only the properties that actually exist in the current Domain implementation.
/// </summary>
public static class TaskMapper
{
    /// <summary>
    /// Converts a Task domain entity to TaskDto.
    /// </summary>
    public static TaskDto ToDto(CrewTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskDto
        {
            Id = task.Id,
            Name = task.Description.Value.Length > 100
                ? string.Concat(task.Description.Value.AsSpan(0, 97), "...")
                : task.Description.Value,
            Description = task.Description,
            ExpectedOutput = task.ExpectedOutput,
            AssignedAgent = task.AssignedAgent?.ToString(),
            Status = MapTaskStatusToString(task.Status),
            Priority = "Normal",
            Dependencies = task.Dependencies.Select(d => d.ToString()).ToImmutableList(),
            RequiredTools = ImmutableList<string>.Empty,
            Output = task.Output != null ? new Task.DTOs.TaskOutputDto
            {
                TaskId = task.Id,
                RawOutput = task.Output.RawOutput ?? string.Empty,
                AgentId = string.Empty,
                CompletedAt = task.CompletedAt ?? DateTime.UtcNow,
                Success = true,
                ExecutionTime = task.GetExecutionTime()
            } : null,
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt
        };
    }

    /// <summary>
    /// Converts a list of Task domain entities to TaskDto list.
    /// </summary>
    public static IReadOnlyList<TaskDto> ToDto(IEnumerable<CrewTask> tasks)
    {
        return tasks.Select(ToDto).ToList();
    }

    /// <summary>
    /// Creates a simple Task from CreateTaskRequest.
    /// </summary>
    public static CrewTask CreateFromRequest(CreateTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.ExpectedOutput))
        {
            throw new ArgumentException("Expected output cannot be empty", nameof(request));
        }

        return new CrewTaskBuilder()
            .Description(TaskDescription.From(request.Description))
            .ExpectedOutput(request.ExpectedOutput)
            .Build();
    }

    /// <summary>
    /// Creates a summary TaskDto for list views.
    /// </summary>
    public static TaskDto ToSummaryDto(CrewTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskDto
        {
            Id = task.Id,
            Name = task.Description.Value.Length > 100
                ? string.Concat(task.Description.Value.AsSpan(0, 97), "...")
                : task.Description.Value,
            Description = task.Description.Value.Length > 100 ?
                string.Concat(task.Description.Value.AsSpan(0, 97), "...") :
                task.Description.Value,
            ExpectedOutput = task.ExpectedOutput,
            Status = MapTaskStatusToString(task.Status),
            Priority = "Normal",
            CreatedAt = task.CreatedAt
        };
    }

    /// <summary>
    /// Converts a list of Task domain entities to summary TaskDto list.
    /// </summary>
    public static IReadOnlyList<TaskDto> ToSummaryDto(IEnumerable<CrewTask> tasks)
    {
        return tasks.Select(ToSummaryDto).ToList();
    }

    private static string MapTaskStatusToString(Domain.Task.ValueObjects.TaskStatus domainStatus)
    {
        return domainStatus.Value switch
        {
            "Pending" => "Pending",
            "InProgress" => "InProgress",
            "Completed" => "Completed",
            "Failed" => "Failed",
            "Cancelled" => "Cancelled",
            "Blocked" => "Blocked",
            _ => "Pending"
        };
    }

    /// <summary>
    /// Converts Application.Execution.TaskOutput to TaskOutputDto.
    /// </summary>
    public static Common.DTOs.TaskOutputDto ToTaskOutputDto(Application.Execution.TaskOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new Common.DTOs.TaskOutputDto
        {
            TaskId = output.TaskId,
            AgentId = output.AgentId ?? string.Empty,
            RawOutput = output.Content,
            CompletedAt = output.CompletedAt,
            Success = output.Success,
            ExecutionTime = output.ExecutionTime,
            ToolsUsed = output.ToolsUsed?.Select(tool => new ToolUsageDto
            {
                ToolId = tool.ToolId,
                ToolName = tool.ToolName,
                Input = tool.Metadata.ToDictionary(),
                Output = tool.Metadata.Get<string>("output") ?? string.Empty,
                Success = tool.Success,
                ExecutionTime = tool.Duration
            }).ToList() ?? []
        };
    }
}
