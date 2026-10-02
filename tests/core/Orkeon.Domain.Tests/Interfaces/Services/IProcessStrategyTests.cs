using Orkeon.Domain.Crew;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using CrewOutput = Orkeon.Domain.Crew.CrewOutput;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Interfaces.Services;

public class IProcessStrategyTests
{
    // Test implementation of IProcessStrategy
    private class TestProcessStrategy : IProcessStrategy
    {
        private readonly Func<Orkeon.Domain.Crew.Crew, Task<CrewOutput>>? _sequentialFunc;
        private readonly Func<Orkeon.Domain.Crew.Crew, AgentId, Task<CrewOutput>>? _hierarchicalFunc;
        private readonly Func<Orkeon.Domain.Crew.Crew, Task<CrewOutput>>? _parallelFunc;

        public int SequentialCallCount { get; private set; }
        public int HierarchicalCallCount { get; private set; }
        public int ParallelCallCount { get; private set; }
        public Orkeon.Domain.Crew.Crew? LastSequentialCrew { get; private set; }
        public Orkeon.Domain.Crew.Crew? LastHierarchicalCrew { get; private set; }
        public AgentId? LastHierarchicalManagerId { get; private set; }
        public Orkeon.Domain.Crew.Crew? LastParallelCrew { get; private set; }
        public IReadOnlyDictionary<string, string>? LastReceivedVariables { get; private set; }

        public TestProcessStrategy(
            Func<Orkeon.Domain.Crew.Crew, Task<CrewOutput>>? sequentialFunc = null,
            Func<Orkeon.Domain.Crew.Crew, AgentId, Task<CrewOutput>>? hierarchicalFunc = null,
            Func<Orkeon.Domain.Crew.Crew, Task<CrewOutput>>? parallelFunc = null)
        {
            _sequentialFunc = sequentialFunc;
            _hierarchicalFunc = hierarchicalFunc;
            _parallelFunc = parallelFunc;
        }

        public async System.Threading.Tasks.Task<CrewOutput> ExecuteSequentialAsync(Orkeon.Domain.Crew.Crew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            SequentialCallCount++;
            LastSequentialCrew = crew;
            LastReceivedVariables = inputVariables;

            if (_sequentialFunc != null)
                return await _sequentialFunc(crew);

            return CreateDefaultOutput("Sequential execution completed");
        }

        public async System.Threading.Tasks.Task<CrewOutput> ExecuteHierarchicalAsync(Orkeon.Domain.Crew.Crew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            HierarchicalCallCount++;
            LastHierarchicalCrew = crew;
            LastHierarchicalManagerId = managerAgentId;
            LastReceivedVariables = inputVariables;

            if (_hierarchicalFunc != null)
                return await _hierarchicalFunc(crew, managerAgentId);

            return CreateDefaultOutput("Hierarchical execution completed");
        }

        public async System.Threading.Tasks.Task<CrewOutput> ExecuteParallelAsync(Orkeon.Domain.Crew.Crew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            ParallelCallCount++;
            LastParallelCrew = crew;
            LastReceivedVariables = inputVariables;

            if (_parallelFunc != null)
                return await _parallelFunc(crew);

            return CreateDefaultOutput("Parallel execution completed");
        }

        public System.Threading.Tasks.Task<CrewOutput> ExecuteAutonomousAsync(Orkeon.Domain.Crew.Crew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Autonomous execution is not covered by this test double.");

        private static CrewOutput CreateDefaultOutput(string message)
        {
            var taskOutputs = new List<TaskOutput>
            {
                TaskOutput.Create("Task 1 output", "text", "raw")
            };

            return CrewOutput.CreateSuccess(
                message,
                null,
                taskOutputs,
                TimeSpan.FromSeconds(1));
        }
    }

    private static Orkeon.Domain.Crew.Crew CreateTestCrew(ProcessType? processType = null)
    {
        processType ??= ProcessType.Sequential;
        var builder = new CrewBuilder()
            .Goal("Test goal")
            .Process(processType);

        // Hierarchical process requires a manager agent
        if (processType == ProcessType.Hierarchical)
        {
            var manager = CreateTestAgent(RoleManager);
            builder.WithManager(manager);
        }

        return builder.Build();
    }

    private static Orkeon.Domain.Agent.Agent CreateTestAgent(string role = RoleManager)
    {
        return new AgentBuilder()
            .Role(role)
            .Goal("Manage tasks")
            .Backstory("Experienced manager")
            .AllowDelegation()
            .Build();
    }

    /// <summary>A crew carrying three task ids — what a strategy runs; no plan travels with it (GAP-31).</summary>
    private static Orkeon.Domain.Crew.Crew CreateTestCrewWithTasks(ProcessType processType)
    {
        var crew = CreateTestCrew(processType);
        for (var i = 0; i < 3; i++)
            crew.AddTask(TaskId.Create());
        return crew;
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteSuccessfully_WhenExecutingSequentialAsyncWithValidInput()
    {
        // Arrange
        var strategy = new TestProcessStrategy();
        var crew = CreateTestCrew(ProcessType.Sequential);

        // Act
        var result = await strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Sequential execution completed", result.Output);
        Assert.Equal(1, strategy.SequentialCallCount);
        Assert.Equal(crew, strategy.LastSequentialCrew);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseCustomLogic_WhenExecutingSequentialAsyncWithCustomFunction()
    {
        // Arrange
        var expectedOutput = CrewOutput.CreateSuccess(
            "Custom sequential output",
            new { custom = "data" },
            [],
            TimeSpan.FromMinutes(2));

        var strategy = new TestProcessStrategy(
            sequentialFunc: crew => System.Threading.Tasks.Task.FromResult(expectedOutput));

        var crew = CreateTestCrew(ProcessType.Sequential);

        // Act
        var result = await strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedOutput, result);
        Assert.Equal("Custom sequential output", result.Output);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFailedOutput_WhenExecutingSequentialAsyncWithFailure()
    {
        // Arrange
        var failedOutput = CrewOutput.CreateFailure(
            "Sequential execution failed",
            [],
            TimeSpan.FromSeconds(1));

        var strategy = new TestProcessStrategy(
            sequentialFunc: crew => System.Threading.Tasks.Task.FromResult(failedOutput));

        var crew = CreateTestCrew(ProcessType.Sequential);

        // Act
        var result = await strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Sequential execution failed", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteSuccessfully_WhenExecutingHierarchicalAsyncWithValidInput()
    {
        // Arrange
        var strategy = new TestProcessStrategy();
        var crew = CreateTestCrew(ProcessType.Hierarchical);
        var managerAgentId = AgentId.Create();

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgentId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Hierarchical execution completed", result.Output);
        Assert.Equal(1, strategy.HierarchicalCallCount);
        Assert.Equal(crew, strategy.LastHierarchicalCrew);
        Assert.Equal(managerAgentId, strategy.LastHierarchicalManagerId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseCustomLogic_WhenExecutingHierarchicalAsyncWithCustomFunction()
    {
        // Arrange
        var taskOutputs = new List<TaskOutput>
        {
            TaskOutput.Create("Manager delegated task 1", "raw", "text"),
            TaskOutput.Create("Manager delegated task 2", "raw", "text")
        };

        var expectedOutput = CrewOutput.CreateSuccess(
            "Manager completed all tasks",
            null,
            taskOutputs,
            TimeoutStandard);

        var strategy = new TestProcessStrategy(
            hierarchicalFunc: (crew, managerAgentId) => System.Threading.Tasks.Task.FromResult(expectedOutput));

        var crew = CreateTestCrew(ProcessType.Hierarchical);
        var managerAgentId = AgentId.Create();

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgentId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedOutput, result);
        Assert.Equal(2, result.TaskOutputs.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldTrackCorrectly_WhenExecutingHierarchicalAsyncWithDifferentManagers()
    {
        // Arrange
        var strategy = new TestProcessStrategy();
        var crew = CreateTestCrew(ProcessType.Hierarchical);
        var managerId1 = AgentId.Create();
        var managerId2 = AgentId.Create();

        // Act
        await strategy.ExecuteHierarchicalAsync(crew, managerId1, cancellationToken: TestContext.Current.CancellationToken);
        await strategy.ExecuteHierarchicalAsync(crew, managerId2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, strategy.HierarchicalCallCount);
        Assert.Equal(managerId2, strategy.LastHierarchicalManagerId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteSuccessfully_WhenExecutingParallelAsyncWithValidInput()
    {
        // Arrange
        var strategy = new TestProcessStrategy();
        var crew = CreateTestCrew(ProcessType.Parallel);

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Parallel execution completed", result.Output);
        Assert.Equal(1, strategy.ParallelCallCount);
        Assert.Equal(crew, strategy.LastParallelCrew);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenExecutingParallelAsyncWithException()
    {
        // Arrange
        var strategy = new TestProcessStrategy(
            parallelFunc: crew => throw new InvalidOperationException("Parallel execution error"));

        var crew = CreateTestCrew(ProcessType.Parallel);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteParallelAsync(crew, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Parallel execution error", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldTrackIndependently_WhenUsingIProcessStrategyWithMultipleExecutions()
    {
        // Arrange
        var strategy = new TestProcessStrategy();
        var sequentialCrew = CreateTestCrew(ProcessType.Sequential);
        var hierarchicalCrew = CreateTestCrew(ProcessType.Hierarchical);
        var parallelCrew = CreateTestCrew(ProcessType.Parallel);
        var managerAgentId = AgentId.Create();

        // Act
        await strategy.ExecuteSequentialAsync(sequentialCrew, cancellationToken: TestContext.Current.CancellationToken);
        await strategy.ExecuteHierarchicalAsync(hierarchicalCrew, managerAgentId, cancellationToken: TestContext.Current.CancellationToken);
        await strategy.ExecuteParallelAsync(parallelCrew, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, strategy.SequentialCallCount);
        Assert.Equal(1, strategy.HierarchicalCallCount);
        Assert.Equal(1, strategy.ParallelCallCount);
        Assert.Equal(sequentialCrew, strategy.LastSequentialCrew);
        Assert.Equal(hierarchicalCrew, strategy.LastHierarchicalCrew);
        Assert.Equal(parallelCrew, strategy.LastParallelCrew);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleFullWorkflow_WhenUsingIProcessStrategyWithCompleteScenario()
    {
        // Arrange
        var executionLog = new List<string>();

        var strategy = new TestProcessStrategy(
            sequentialFunc: async crew =>
            {
                executionLog.Add("Sequential started");
                await System.Threading.Tasks.Task.Delay(10);
                executionLog.Add("Sequential completed");

                var outputs = crew.Tasks.Select(t =>
                    TaskOutput.Create($"Sequential task {t}", "raw", "text")).ToList();

                return CrewOutput.CreateSuccess(
                    "Sequential workflow done",
                    null,
                    outputs,
                    TimeSpan.FromMilliseconds(100));
            },
            hierarchicalFunc: async (crew, managerAgentId) =>
            {
                executionLog.Add($"Hierarchical started with {managerAgentId}");
                await System.Threading.Tasks.Task.Delay(10);
                executionLog.Add("Hierarchical completed");

                return CrewOutput.CreateSuccess(
                    $"Hierarchical workflow done by {managerAgentId}",
                    null,
                    [],
                    TimeSpan.FromMilliseconds(150));
            },
            parallelFunc: async crew =>
            {
                executionLog.Add("Parallel started");

                foreach (var task in crew.Tasks)
                {
                    executionLog.Add($"Processing task {task}");
                    await System.Threading.Tasks.Task.Delay(5);
                }

                executionLog.Add("Parallel completed");

                return CrewOutput.CreateSuccess(
                    $"Parallel workflow done with {crew.Tasks.Count} tasks",
                    null,
                    [],
                    TimeSpan.FromMilliseconds(200));
            });

        var managerAgentId = AgentId.Create();

        // Act - Execute different strategies
        var sequentialResult = await strategy.ExecuteSequentialAsync(
            CreateTestCrewWithTasks(ProcessType.Sequential), cancellationToken: TestContext.Current.CancellationToken);

        var hierarchicalResult = await strategy.ExecuteHierarchicalAsync(
            CreateTestCrew(ProcessType.Hierarchical),
            managerAgentId, cancellationToken: TestContext.Current.CancellationToken);

        var parallelResult = await strategy.ExecuteParallelAsync(
            CreateTestCrewWithTasks(ProcessType.Parallel), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.All([sequentialResult, hierarchicalResult, parallelResult],
            result => Assert.True(result.Success));

        Assert.Equal(3, sequentialResult.TaskOutputs.Count);
        Assert.Contains(managerAgentId.ToString(), hierarchicalResult.Output);
        Assert.Contains("3 tasks", parallelResult.Output);

        Assert.Equal(9, executionLog.Count);
        Assert.Contains("Sequential started", executionLog);
        Assert.StartsWith("Hierarchical started with", executionLog[2]);
        Assert.StartsWith("Processing task ", executionLog[5]);
    }
}
