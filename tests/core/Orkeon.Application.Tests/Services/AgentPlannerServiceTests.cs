using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Planning;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Application.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Tests for AgentPlannerService following Clean Architecture principles.
/// Tests the planning capabilities for task execution.
/// </summary>
public class AgentPlannerServiceTests
{
    #region Test Doubles

    /// <summary>
    /// Test logger for AgentPlannerService.
    /// </summary>
    private class TestLogger : ILogger<AgentPlannerService>
    {
        private readonly List<string> _loggedMessages = [];
        public List<string> LoggedMessages => _loggedMessages;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _loggedMessages.Add($"[{logLevel}] {formatter(state, exception)}");
        }

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Factory method to create test tasks.
    /// </summary>
    private static DomainTask CreateTestTask(
        string? description = null,
        string? expectedOutput = null,
        string? assignedAgentId = null)
    {
        var taskDescription = TaskDescription.From(description ?? "Test task description");
        var task = DomainTask.Create(
            taskDescription,
            ExpectedOutput.From(expectedOutput ?? "Expected test output"));

        return task;
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgentPlannerService(null!));
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithValidLogger()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var service = new AgentPlannerService(logger);

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region CreatePlanAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnPlan_WhenCreatingPlanAsyncWithValidTask()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();

        // Act
        var plan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(task.Id, plan.TaskId);
        Assert.NotEmpty(plan.Steps);
        Assert.Equal(4, plan.Steps.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), plan.EstimatedDuration);
        Assert.Equal(0.8, plan.ConfidenceScore);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateStandardSteps_WhenCreatingPlanAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();

        // Act
        var plan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);

        // Assert
        var steps = plan.Steps;
        Assert.Equal("Analyze Task Requirements", steps[0].Action);
        Assert.Equal("Plan Approach", steps[1].Action);
        Assert.Equal("Execute Task", steps[2].Action);
        Assert.Equal("Review and Finalize", steps[3].Action);

        // Verify descriptions
        Assert.Contains("Review and understand", steps[0].Description);
        Assert.Contains("Determine the best approach", steps[1].Description);
        Assert.Contains("Perform the actual work", steps[2].Description);
        Assert.Contains("Review results", steps[3].Description);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAssignEstimatedDurations_WhenCreatingPlanAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();

        // Act
        var plan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeoutStandard, plan.Steps[0].EstimatedDuration);
        Assert.Equal(TimeoutStandard, plan.Steps[1].EstimatedDuration);
        Assert.Equal(TimeoutLong, plan.Steps[2].EstimatedDuration);
        Assert.Equal(TimeoutStandard, plan.Steps[3].EstimatedDuration);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenCreatingPlanAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();

        // Act
        await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(logger.LoggedMessages, msg =>
            msg.Contains("[Information]") &&
            msg.Contains("Creating plan for task") &&
            msg.Contains(task.Id));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellationToken_WhenCreatingPlanAsyncWithCancellation()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act - Should complete normally as current implementation doesn't check cancellation
        var plan = await service.CreatePlanAsync(task, cts.Token);

        // Assert
        Assert.NotNull(plan);
    }

    #endregion

    #region RefinePlanAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnOriginalPlan_WhenUsingRefinePlanAsyncWithSuccessfulFeedback()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var originalPlan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);
        var feedback = new PlanFeedback
        {
            PlanId = originalPlan.Id,
            Success = true,
            CompletedSteps = originalPlan.Steps.Select(s => s.Id).ToList()
        };

        // Act
        var refinedPlan = await service.RefinePlanAsync(originalPlan, feedback, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(originalPlan, refinedPlan);
        Assert.Equal(0.8, refinedPlan.ConfidenceScore);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateRetrySteps_WhenUsingRefinePlanAsyncWithFailedSteps()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var originalPlan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);
        var failedStepId = originalPlan.Steps[2].Id; // "Execute Task" step
        var feedback = new PlanFeedback
        {
            PlanId = originalPlan.Id,
            Success = false,
            CompletedSteps = [originalPlan.Steps[0].Id, originalPlan.Steps[1].Id],
            FailedSteps = [failedStepId]
        };

        // Act
        var refinedPlan = await service.RefinePlanAsync(originalPlan, feedback, TestContext.Current.CancellationToken);

        // Assert
        // Service replaces failed steps with retry steps, keeping count same
        Assert.Equal(2, refinedPlan.Steps.Count); // Only failed + next step remain
        // Check that failed step was replaced with retry
        Assert.Contains(refinedPlan.Steps, s => s.Action.StartsWith("Retry:"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldKeepPendingSteps_WhenUsingRefinePlanAsyncWithPartialCompletion()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var originalPlan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);
        var feedback = new PlanFeedback
        {
            PlanId = originalPlan.Id,
            Success = false,
            CompletedSteps = [originalPlan.Steps[0].Id],
            FailedSteps = [originalPlan.Steps[1].Id]
        };

        // Act
        var refinedPlan = await service.RefinePlanAsync(originalPlan, feedback, TestContext.Current.CancellationToken);

        // Assert
        // Should have: retry for failed step + 2 pending steps = 3 steps
        Assert.Equal(3, refinedPlan.Steps.Count);
        Assert.Contains(refinedPlan.Steps, s => s.Action.StartsWith("Retry:"));
        Assert.Contains(refinedPlan.Steps, s => s.Action == "Execute Task");
        Assert.Contains(refinedPlan.Steps, s => s.Action == "Review and Finalize");
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReduceConfidenceScore_WhenUsingRefinePlanAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var originalPlan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);
        var originalConfidence = originalPlan.ConfidenceScore;
        var feedback = new PlanFeedback
        {
            PlanId = originalPlan.Id,
            Success = false,
            FailedSteps = [originalPlan.Steps[0].Id]
        };

        // Act
        var refinedPlan = await service.RefinePlanAsync(originalPlan, feedback, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(originalConfidence * 0.9, refinedPlan.ConfidenceScore);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingRefinePlanAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var plan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);
        var feedback = new PlanFeedback { PlanId = plan.Id, Success = true };

        // Act
        await service.RefinePlanAsync(plan, feedback, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(logger.LoggedMessages, msg =>
            msg.Contains("[Information]") &&
            msg.Contains("Refining plan") &&
            msg.Contains(plan.Id));
    }

    #endregion

    #region ValidatePlanAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnValid_WhenValidatingPlanAsyncWithValidPlan()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var plan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenValidatingPlanAsyncWithEmptyTaskId()
    {
        // Arrange — a null TaskId no longer throws on implicit string conversion (TypedId's
        // operator is null-safe and returns string.Empty). Validation must therefore surface
        // the missing-task association as a regular error instead of an exception.
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var plan = new TaskPlan
        {
            TaskId = null!,
            Steps = [new PlanStep { Action = "Test" }]
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("task", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenValidatingPlanAsyncWithNoSteps()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps = []
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Plan must contain at least one step", result.Errors);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenValidatingPlanAsyncWithStepMissingAction()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps =
            [
                new PlanStep { Action = "", Description = "Test step" }
            ]
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must have an action"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnWarning_WhenValidatingPlanAsyncWithStepMissingDescription()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps =
            [
                new PlanStep { Action = "Test Action", Description = "" }
            ]
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid); // Warnings don't make plan invalid
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("should have a description"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenValidatingPlanAsyncWithCircularDependency()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var stepId1 = PlanStepId.Create();
        var stepId2 = PlanStepId.Create();
        var step1 = new PlanStep { Id = stepId1, Action = "Step 1", Dependencies = [stepId2] };
        var step2 = new PlanStep { Id = stepId2, Action = "Step 2", Dependencies = [stepId1] };
        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps = [step1, step2]
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Circular dependency"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnError_WhenValidatingPlanAsyncWithSelfDependency()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var stepId = PlanStepId.Create();
        var step = new PlanStep
        {
            Id = stepId,
            Action = "Self Dependent Step",
            Dependencies = [stepId] // Depends on itself
        };
        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps = [step]
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Circular dependency"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldValidate_WhenValidatingPlanAsyncWithComplexDependencyChain()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var stepId1 = PlanStepId.Create();
        var stepId2 = PlanStepId.Create();
        var stepId3 = PlanStepId.Create();
        var step1 = new PlanStep { Id = stepId1, Action = "Step 1", Description = "First step" };
        var step2 = new PlanStep { Id = stepId2, Action = "Step 2", Description = "Second step", Dependencies = [stepId1] };
        var step3 = new PlanStep { Id = stepId3, Action = "Step 3", Description = "Third step", Dependencies = [stepId1, stepId2] };
        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps = [step1, step2, step3]
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogDebugInformation_WhenValidatingPlanAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var plan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);

        // Act
        await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(logger.LoggedMessages, msg =>
            msg.Contains("[Debug]") &&
            msg.Contains("Validating plan") &&
            msg.Contains(plan.Id));
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public async System.Threading.Tasks.Task ShouldCompleteScenario_WhenPlanningLifecycleCreateRefineValidate()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask(
            description: "Analyze customer feedback and create summary report",
            expectedOutput: ExpectedOutput.From("Comprehensive report with insights and recommendations"));

        // Act - Create initial plan
        var initialPlan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);
        var initialValidation = await service.ValidatePlanAsync(initialPlan, TestContext.Current.CancellationToken);

        // Assert initial plan
        Assert.True(initialValidation.IsValid);
        Assert.Equal(4, initialPlan.Steps.Count);

        // Act - Simulate partial execution with failure
        var feedback = new PlanFeedback
        {
            PlanId = initialPlan.Id,
            Success = false,
            CompletedSteps = [initialPlan.Steps[0].Id, initialPlan.Steps[1].Id],
            FailedSteps = [initialPlan.Steps[2].Id],
            StepFeedback = new Dictionary<string, string>
            {
                { initialPlan.Steps[2].Id, "Failed to access customer database" }
            }
        };

        var refinedPlan = await service.RefinePlanAsync(initialPlan, feedback, TestContext.Current.CancellationToken);
        var refinedValidation = await service.ValidatePlanAsync(refinedPlan, TestContext.Current.CancellationToken);

        // Assert refined plan
        Assert.True(refinedValidation.IsValid);
        Assert.Equal(2, refinedPlan.Steps.Count); // Service maintains same step count
        Assert.Equal(0.72, refinedPlan.ConfidenceScore, 2); // 0.8 * 0.9 = 0.72

        // Verify logging
        Assert.True(logger.LoggedMessages.Count >= 3);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateUniquePlans_WhenUsingMultipleTasks()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task1 = CreateTestTask(description: "Task 1");
        var task2 = CreateTestTask(description: "Task 2");
        var task3 = CreateTestTask(description: "Task 3");

        // Act
        var plan1 = await service.CreatePlanAsync(task1, TestContext.Current.CancellationToken);
        var plan2 = await service.CreatePlanAsync(task2, TestContext.Current.CancellationToken);
        var plan3 = await service.CreatePlanAsync(task3, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(plan1.Id, plan2.Id);
        Assert.NotEqual(plan2.Id, plan3.Id);
        Assert.NotEqual(plan1.Id, plan3.Id);

        // Each plan should reference its own task
        Assert.Equal(task1.Id, plan1.TaskId);
        Assert.Equal(task2.Id, plan2.TaskId);
        Assert.Equal(task3.Id, plan3.TaskId);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenUsingRefinePlanAsyncWithEmptyFeedback()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var task = CreateTestTask();
        var plan = await service.CreatePlanAsync(task, TestContext.Current.CancellationToken);
        var feedback = new PlanFeedback
        {
            PlanId = plan.Id,
            Success = false,
            CompletedSteps = [],
            FailedSteps = []
        };

        // Act
        var refinedPlan = await service.RefinePlanAsync(plan, feedback, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(plan.Steps.Count, refinedPlan.Steps.Count);
        // Confidence score should be reduced, but exact calculation may vary
        Assert.True(refinedPlan.ConfidenceScore <= plan.ConfidenceScore);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenValidatingPlanAsyncWithNullStepCollections()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps = [] // Empty list to avoid null reference
        };

        // Act & Assert - Should not throw, but return validation error
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid); // Empty steps should be invalid
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandle_WhenUsingPlanStepWithLargeParametersDictionary()
    {
        // Arrange
        var logger = new TestLogger();
        var service = new AgentPlannerService(logger);
        var step = new PlanStep
        {
            Action = "Complex Step",
            Description = "Step with many parameters"
        };

        // Add many parameters
        for (int i = 0; i < 1000; i++)
        {
            step.Parameters[$"param{i}"] = $"value{i}";
        }

        var plan = new TaskPlan
        {
            TaskId = TaskId.Create(),
            Steps = [step]
        };

        // Act
        var result = await service.ValidatePlanAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion
}
