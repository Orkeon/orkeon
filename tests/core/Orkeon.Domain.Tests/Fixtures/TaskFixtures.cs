using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Fixtures;

/// <summary>
/// Test fixtures for Task testing following Clean Architecture patterns.
/// Provides pre-configured tasks and data for consistent testing.
/// </summary>
public static class TaskFixtures
{
    /// <summary>
    /// Creates a basic task with minimal required properties.
    /// </summary>
    public static DomainTask CreateBasicTask(
        string description = "Research market trends",
        string expectedOutput = "Comprehensive market analysis report")
    {
        return new CrewTaskBuilder()
            .Description(description)
            .ExpectedOutput(expectedOutput)
            .Build();
    }

    /// <summary>
    /// Creates a task using the static factory method.
    /// </summary>
    public static DomainTask CreateTaskWithFactory(
        string description = GoalAnalyzeData,
        string expectedOutput = "Data insights")
    {
        return new CrewTaskBuilder()
            .Description(description)
            .ExpectedOutput(expectedOutput)
            .Build();
    }

    /// <summary>
    /// Creates a task with sequential dependencies.
    /// </summary>
    public static DomainTask CreateTaskWithSequentialDependencies(params DomainTask[] dependsOn)
    {
        var builder = new CrewTaskBuilder()
            .Description("Process dependent results")
            .ExpectedOutput("Aggregated output");

        foreach (var dep in dependsOn)
        {
            builder.DependsOn(dep);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a task with parallel dependencies.
    /// </summary>
    public static DomainTask CreateTaskWithParallelDependencies(params DomainTask[] parallelWith)
    {
        return new CrewTaskBuilder()
            .Description("Parallel processing task")
            .ExpectedOutput("Parallel results")
            .Build();
    }

    /// <summary>
    /// Creates a task with custom dependencies.
    /// </summary>
    public static DomainTask CreateTaskWithCustomDependencies(params TaskDependency[] dependencies)
    {
        return new CrewTaskBuilder()
            .Description("Complex task with dependencies")
            .ExpectedOutput("Complex output")
            .Build();
    }

    /// <summary>
    /// Creates a task assigned to an agent.
    /// </summary>
    public static DomainTask CreateAssignedTask(DomainAgent? agent = null)
    {
        var builder = new CrewTaskBuilder()
            .Description("Assigned task")
            .ExpectedOutput("Task output");

        if (agent != null)
        {
            builder.AssignTo(agent);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a task configured for async execution.
    /// </summary>
    public static DomainTask CreateAsyncTask()
    {
        return new CrewTaskBuilder()
            .Description("Async processing task")
            .ExpectedOutput("Async results")
            .Async()
            .Build();
    }

    /// <summary>
    /// Creates a task with specific output format.
    /// </summary>
    public static DomainTask CreateTaskWithOutputFormat(OutputFormat format)
    {
        return new CrewTaskBuilder()
            .Description($"Task with {format} output")
            .ExpectedOutput($"{format} formatted output")
            .Build();
    }

    /// <summary>
    /// Creates a task with context variables.
    /// </summary>
    public static DomainTask CreateTaskWithContext(Dictionary<string, string>? context = null)
    {
        var builder = new CrewTaskBuilder()
            .Description("Contextual task")
            .ExpectedOutput("Context-aware output");

        if (context != null)
        {
            foreach (var kvp in context)
            {
                builder.WithContext(kvp.Key, kvp.Value);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a cancelled task for testing.
    /// </summary>
    public static DomainTask CreateCancelledTask()
    {
        return CreateBasicTask("Cancelled task", "Never executed");
    }

    /// <summary>
    /// Creates a chain of dependent tasks.
    /// </summary>
    public static List<DomainTask> CreateTaskChain(int count = 3)
    {
        var tasks = new List<DomainTask>();

        for (int i = 0; i < count; i++)
        {
            var builder = new CrewTaskBuilder()
                .Description($"Task {i + 1} in chain")
                .ExpectedOutput($"Output from task {i + 1}");

            if (i > 0)
            {
                builder.DependsOn(tasks[i - 1]);
            }

            tasks.Add(builder.Build());
        }

        return tasks;
    }

    /// <summary>
    /// Test data for task validation.
    /// </summary>
    public static class TestData
    {
        public static IEnumerable<object[]> ValidTaskDescriptions =>
            [
                ["Research market trends", "Market analysis report"],
                ["Implement feature X", "Feature X implemented and tested"],
                ["Review code changes", "Code review feedback"],
                ["Write documentation", "Complete documentation"],
                ["Deploy to production", "Deployment successful"]
            ];

        public static IEnumerable<object[]> OutputFormats =>
            [
                [OutputFormat.Text],
                [OutputFormat.Json],
                [OutputFormat.Custom]
            ];

        public static IEnumerable<object[]> DependencyTypes =>
            [
                [DependencyType.Hard],
                [DependencyType.Soft],
                [DependencyType.Timed]
            ];
    }
}
