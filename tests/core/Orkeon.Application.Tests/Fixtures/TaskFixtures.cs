using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using TaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Application.Tests.Fixtures;

/// <summary>
/// Provides test fixtures for creating domain tasks.
/// </summary>
public static class TaskFixtures
{
    public static DomainTask CreateBasicTask(
        string? description = null,
        string? expectedOutput = null)
    {
        return DomainTask.Create(
            description: TaskDescription.From(description ?? "Test task description"),
            expectedOutput: ExpectedOutput.From(expectedOutput ?? "Expected test output")
        );
    }

    public static DomainTask CreateTaskWithPriority(
        string description,
        TaskPriority priority)
    {
        var task = DomainTask.Create(
            description: TaskDescription.From(description),
            expectedOutput: ExpectedOutput.From("Complete the task successfully"),
            priority: priority
        );

        return task;
    }

    public static DomainTask CreateTaskWithDependencies(
        string description,
        params TaskId[] dependencies)
    {
        var task = DomainTask.Create(
            description: TaskDescription.From(description),
            expectedOutput: ExpectedOutput.From("Complete after dependencies")
        );

        foreach (var dependency in dependencies)
        {
            task.AddDependency(dependency);
        }

        return task;
    }

    public static DomainTask CreateTaskWithSkills(
        string description,
        params string[] requiredSkills)
    {
        return DomainTask.Create(
            description: TaskDescription.From(description),
            expectedOutput: ExpectedOutput.From("Task completed with required skills")
        );
    }

    public static DomainTask CreateTaskWithTools(
        string description,
        params ToolId[] toolIds)
    {
        var task = DomainTask.Create(
            description: TaskDescription.From(description),
            expectedOutput: ExpectedOutput.From("Task completed using tools")
        );

        foreach (var toolId in toolIds)
        {
            task.AddRequiredTool(toolId);
        }

        return task;
    }

    public static DomainTask CreateResearchTask()
    {
        return DomainTask.Create(
            description: TaskDescription.From("Research the latest trends in AI and machine learning"),
            expectedOutput: ExpectedOutput.From("A comprehensive report on AI/ML trends with key insights"),
            priority: TaskPriority.High
        );
    }

    public static DomainTask CreateDevelopmentTask()
    {
        return DomainTask.Create(
            description: TaskDescription.From("Implement user authentication feature"),
            expectedOutput: ExpectedOutput.From("Working authentication system with login/logout functionality"),
            priority: TaskPriority.Critical
        );
    }

    public static DomainTask CreateTaskWithContext(
        string description,
        Dictionary<string, object> context)
    {
        return DomainTask.Create(
            description: TaskDescription.From(description),
            expectedOutput: ExpectedOutput.From("Task completed with context")
        );
    }

    public static DomainTask CreateAsyncTask()
    {
        return DomainTask.Create(
            description: TaskDescription.From("Process large dataset asynchronously"),
            expectedOutput: ExpectedOutput.From("Dataset processed and results stored"),
            priority: TaskPriority.Normal
        );
    }

    public static DomainTask CreateHumanInputTask()
    {
        return DomainTask.Create(
            description: TaskDescription.From("Review and approve the proposal"),
            expectedOutput: ExpectedOutput.From("Approved proposal with feedback"),
            priority: TaskPriority.Normal
        );
    }

    public static List<DomainTask> CreateTaskChain()
    {
        var task1 = CreateBasicTask("Step 1: Gather requirements");
        var task2 = CreateBasicTask("Step 2: Design solution");
        var task3 = CreateBasicTask("Step 3: Implement solution");
        var task4 = CreateBasicTask("Step 4: Test and deploy");

        // Create dependency chain
        task2.AddDependency(task1.Id);
        task3.AddDependency(task2.Id);
        task4.AddDependency(task3.Id);

        return [task1, task2, task3, task4];
    }

    public static DomainTask CreateCompletedTask(AgentId agentId)
    {
        var task = CreateBasicTask("Completed task");
        task.AssignTo(agentId);
        task.Start(agentId);
        task.Complete(agentId, TaskOutput.Text("Task successfully completed"));
        return task;
    }

    public static DomainTask CreateFailedTask(AgentId agentId)
    {
        var task = CreateBasicTask("Failed task");
        task.AssignTo(agentId);
        task.Start(agentId);
        task.Fail("Task failed due to error");
        return task;
    }

    public static DomainTask CreateInProgressTask(AgentId agentId)
    {
        var task = CreateBasicTask("In progress task");
        task.AssignTo(agentId);
        task.Start(agentId);
        return task;
    }

    public static DomainTask CreateTaskWithFullConfiguration()
    {
        var task = DomainTask.Create(
            description: TaskDescription.From("Comprehensive task with all features"),
            expectedOutput: ExpectedOutput.From("Complete deliverable with all requirements met"),
            priority: TaskPriority.High
        );

        // Add tools
        task.AddRequiredTool(ToolId.From(Guid.NewGuid()));
        task.AddRequiredTool(ToolId.From(Guid.NewGuid()));

        return task;
    }
}
