using Orkeon.Domain.Memory.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Application.Common.Mapping;
using Orkeon.Application.Task.DTOs;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.DTOs.Mapping;

public class TaskMapperTests
{
    [Fact]
    public void ShouldMapAllProperties_WhenUsingToDtoWithCompleteTask()
    {
        var task = DomainTask.Create(
            TaskDescription.From("Implement user authentication"),
            ExpectedOutput.From("Working auth system with JWT tokens"));

        var dto = TaskMapper.ToDto(task);

        Assert.NotNull(dto);
        Assert.Equal(task.Id, dto.Id);
        Assert.Equal("Implement user authentication", dto.Description);
        Assert.Equal("Working auth system with JWT tokens", dto.ExpectedOutput);
        Assert.Null(dto.AssignedAgent);
        Assert.Equal("Pending", dto.Status);
        Assert.Null(dto.Output);
        Assert.Equal("Normal", dto.Priority);
        Assert.Empty(dto.Dependencies);
        Assert.Null(dto.StartedAt);
        Assert.Null(dto.CompletedAt);
        // The mapper's contract is that it copies the aggregate's own timestamp; comparing it
        // to a freshly read DateTime.UtcNow measured the wall clock instead of the mapping,
        // and flipped under parallel load.
        Assert.Equal(task.CreatedAt, dto.CreatedAt);
    }

    [Fact]
    public void ShouldMapStatusCorrectly_WhenUsingToDtoWithTaskInDifferentStatuses()
    {
        var pendingTask = DomainTask.Create(
            TaskDescription.From("Pending task"), ExpectedOutput.From("Output"));

        var inProgressTask = DomainTask.Create(
            TaskDescription.From("In progress task"), ExpectedOutput.From("Output"));
        var agentId = Orkeon.Domain.Common.AgentId.Create();
        inProgressTask.AssignTo(agentId);
        inProgressTask.Start(agentId);

        var completedTask = DomainTask.Create(
            TaskDescription.From("Completed task"), ExpectedOutput.From("Output"));
        var completedAgentId = Orkeon.Domain.Common.AgentId.Create();
        completedTask.AssignTo(completedAgentId);
        completedTask.Start(completedAgentId);
        completedTask.Complete(completedAgentId,
            Orkeon.Domain.Task.ValueObjects.TaskOutput.Create("Task completed successfully", "text"));

        var pendingDto = TaskMapper.ToDto(pendingTask);
        var inProgressDto = TaskMapper.ToDto(inProgressTask);
        var completedDto = TaskMapper.ToDto(completedTask);

        Assert.Equal("Pending", pendingDto.Status);
        Assert.Equal("InProgress", inProgressDto.Status);
        Assert.Equal("Completed", completedDto.Status);
    }

    [Fact]
    public void ShouldCreateTask_WhenUsingCreateFromRequestWithValidRequest()
    {
        var request = new CreateTaskRequest
        {
            Description = "Generate API documentation",
            ExpectedOutput = ExpectedOutput.From("Complete API docs in markdown")
        };

        var task = TaskMapper.CreateFromRequest(request);

        Assert.NotNull(task);
        Assert.Equal("Generate API documentation", task.Description.Value);
        Assert.Equal("Complete API docs in markdown", task.ExpectedOutput);
        Assert.Equal(Domain.Task.ValueObjects.TaskStatus.Pending, task.Status);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingCreateFromRequestWithNullExpectedOutput()
    {
        var request = new CreateTaskRequest
        {
            Description = "Simple task",
            ExpectedOutput = null
        };

        var exception = Assert.Throws<ArgumentException>(() => TaskMapper.CreateFromRequest(request));
        Assert.Equal("request", exception.ParamName);
        Assert.Contains("Expected output cannot be empty", exception.Message);
    }

    [Fact]
    public void ShouldTruncate_WhenUsingToSummaryDtoWithLongDescription()
    {
        var longDescription = "This is a very long task description that exceeds 100 characters and should be truncated when creating a summary DTO for list views";
        var task = DomainTask.Create(TaskDescription.From(longDescription), ExpectedOutput.From("Output"));

        var summaryDto = TaskMapper.ToSummaryDto(task);

        Assert.NotNull(summaryDto);
        Assert.True(summaryDto.Description.Length <= 100);
        Assert.EndsWith("...", summaryDto.Description);
        Assert.Equal(task.Id, summaryDto.Id);
        Assert.Equal("Pending", summaryDto.Status);
        Assert.Equal("Normal", summaryDto.Priority);
    }

    [Fact]
    public void ShouldNotTruncate_WhenUsingToSummaryDtoWithShortDescription()
    {
        var task = DomainTask.Create(TaskDescription.From("Short task"), ExpectedOutput.From("Output"));

        var summaryDto = TaskMapper.ToSummaryDto(task);

        Assert.NotNull(summaryDto);
        Assert.Equal("Short task", summaryDto.Description);
        Assert.DoesNotContain("...", summaryDto.Description);
    }

    [Fact]
    public void ShouldMapAllTasks_WhenUsingToDtoWithMultipleTasks()
    {
        var tasks = new List<DomainTask>
        {
            DomainTask.Create(TaskDescription.From("Task 1"), ExpectedOutput.From("Output 1")),
            DomainTask.Create(TaskDescription.From("Task 2"), ExpectedOutput.From("Output 2")),
            DomainTask.Create(TaskDescription.From("Task 3"), ExpectedOutput.From("Output 3"))
        };

        var dtos = TaskMapper.ToDto(tasks);

        Assert.Equal(3, dtos.Count);
        Assert.Equal("Task 1", dtos[0].Description);
        Assert.Equal("Task 2", dtos[1].Description);
        Assert.Equal("Task 3", dtos[2].Description);
        Assert.All(dtos, dto => Assert.Equal("Pending", dto.Status));
    }

    [Fact]
    public void ShouldMapAllTasks_WhenUsingToSummaryDtoWithMultipleTasks()
    {
        var tasks = new List<DomainTask>
        {
            DomainTask.Create(TaskDescription.From("Summary task 1"), ExpectedOutput.From("Output 1")),
            DomainTask.Create(TaskDescription.From("Summary task 2"), ExpectedOutput.From("Output 2"))
        };

        var summaryDtos = TaskMapper.ToSummaryDto(tasks);

        Assert.Equal(2, summaryDtos.Count);
        Assert.Equal("Summary task 1", summaryDtos[0].Description);
        Assert.Equal("Summary task 2", summaryDtos[1].Description);
        Assert.All(summaryDtos, dto => Assert.Equal("Normal", dto.Priority));
    }

    [Fact]
    public void ShouldMapCorrectly_WhenUsingMapTaskStatusWithAllDomainStatuses()
    {
        Assert.Equal("Pending", GetMappedStatus(Domain.Task.ValueObjects.TaskStatus.Pending));
        Assert.Equal("InProgress", GetMappedStatus(Domain.Task.ValueObjects.TaskStatus.InProgress));
        Assert.Equal("Completed", GetMappedStatus(Domain.Task.ValueObjects.TaskStatus.Completed));
        Assert.Equal("Failed", GetMappedStatus(Domain.Task.ValueObjects.TaskStatus.Failed));
        Assert.Equal("Cancelled", GetMappedStatus(Domain.Task.ValueObjects.TaskStatus.Cancelled));
    }

    [Fact]
    public void ShouldMapAllProperties_WhenUsingToTaskOutputDtoWithCompleteTaskOutput()
    {
        var toolUsage = new Orkeon.Domain.Tools.ToolUsage(
            new Orkeon.Domain.Tools.ToolCallIdentity("tool-123", "FileReader", AgentId1, TaskId1),
            duration: TimeSpan.FromSeconds(2), success: true, error: null,
            metadata: ToolUsageMetadata.CreateBuilder().Add("file", "test.txt").Build());

        var taskOutput = new Application.Execution.TaskOutput(
            TaskId: Orkeon.Domain.Common.TaskId.Create().ToString(),
            AgentId: Orkeon.Domain.Common.AgentId.Create().ToString(),
            Content: "Task completed successfully", CompletedAt: DateTime.UtcNow,
            Success: true, ExecutionTime: TimeoutStandard, ToolsUsed: [toolUsage]);

        var dto = TaskMapper.ToTaskOutputDto(taskOutput);

        Assert.NotNull(dto);
        Assert.Equal(taskOutput.TaskId, dto.TaskId);
        Assert.Equal(taskOutput.AgentId, dto.AgentId);
        Assert.Equal("Task completed successfully", dto.Content);
        Assert.Equal(taskOutput.CompletedAt, dto.CompletedAt);
        Assert.True(dto.Success);
        Assert.Equal(TimeoutStandard, dto.ExecutionTime);
        Assert.Single(dto.ToolsUsed);
        var toolDto = dto.ToolsUsed[0];
        Assert.Equal("tool-123", toolDto.ToolId);
        Assert.Equal("FileReader", toolDto.ToolName);
        Assert.Contains("file", toolDto.Input.Keys);
        Assert.Equal("test.txt", toolDto.Input["file"]);
        Assert.Equal(string.Empty, toolDto.Output);
        Assert.True(toolDto.Success);
        Assert.Equal(TimeSpan.FromSeconds(2), toolDto.ExecutionTime);
    }

    [Fact]
    public void ShouldMapError_WhenUsingToTaskOutputDtoWithFailedToolUsage()
    {
        var failedToolUsage = new Orkeon.Domain.Tools.ToolUsage(
            new Orkeon.Domain.Tools.ToolCallIdentity("tool-456", ToolDatabaseQuery, AgentId1, TaskId1),
            duration: TimeSpan.FromSeconds(1), success: false,
            error: "Connection timeout", metadata: ToolUsageMetadata.Empty);

        var taskOutput = new Application.Execution.TaskOutput(
            TaskId: Orkeon.Domain.Common.TaskId.Create().ToString(),
            AgentId: Orkeon.Domain.Common.AgentId.Create().ToString(),
            Content: "Task failed", CompletedAt: DateTime.UtcNow,
            Success: false, ExecutionTime: TimeSpan.FromMinutes(1),
            ToolsUsed: [failedToolUsage]);

        var dto = TaskMapper.ToTaskOutputDto(taskOutput);

        Assert.NotNull(dto);
        Assert.False(dto.Success);
        var toolDto = dto.ToolsUsed[0];
        Assert.Equal(string.Empty, toolDto.Output);
        Assert.False(toolDto.Success);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenUsingToTaskOutputDtoWithNullToolsUsed()
    {
        var taskOutput = new Application.Execution.TaskOutput(
            TaskId: Orkeon.Domain.Common.TaskId.Create().ToString(),
            AgentId: Orkeon.Domain.Common.AgentId.Create().ToString(),
            Content: "Simple task", CompletedAt: DateTime.UtcNow,
            Success: true, ExecutionTime: TimeSpan.FromMinutes(1), ToolsUsed: null);

        var dto = TaskMapper.ToTaskOutputDto(taskOutput);

        Assert.NotNull(dto);
        Assert.Empty(dto.ToolsUsed);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenUsingToDtoWithEmptyTaskList()
    {
        var dtos = TaskMapper.ToDto(new List<DomainTask>());
        Assert.NotNull(dtos);
        Assert.Empty(dtos);
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenUsingToSummaryDtoWithEmptyTaskList()
    {
        var summaryDtos = TaskMapper.ToSummaryDto(new List<DomainTask>());
        Assert.NotNull(summaryDtos);
        Assert.Empty(summaryDtos);
    }

    private static string GetMappedStatus(Domain.Task.ValueObjects.TaskStatus domainStatus)
    {
        var task = DomainTask.Create(TaskDescription.From("Test"), ExpectedOutput.From("Output"));
        var helperAgentId = Orkeon.Domain.Common.AgentId.Create();

        if (domainStatus == Domain.Task.ValueObjects.TaskStatus.InProgress)
        {
            task.AssignTo(helperAgentId);
            task.Start(helperAgentId);
        }
        else if (domainStatus == Domain.Task.ValueObjects.TaskStatus.Completed)
        {
            task.AssignTo(helperAgentId);
            task.Start(helperAgentId);
            task.Complete(helperAgentId, Orkeon.Domain.Task.ValueObjects.TaskOutput.Create("Done", "text"));
        }
        else if (domainStatus == Domain.Task.ValueObjects.TaskStatus.Failed)
        {
            task.Fail("Error");
        }
        else if (domainStatus == Domain.Task.ValueObjects.TaskStatus.Cancelled)
        {
            task.Cancel("Cancelled for testing");
        }

        return TaskMapper.ToDto(task).Status;
    }
}
