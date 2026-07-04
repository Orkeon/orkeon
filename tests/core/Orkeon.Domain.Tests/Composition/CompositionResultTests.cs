using Orkeon.Domain.Agent.Composition;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainTask = Orkeon.Domain.Task.CrewTask;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
namespace Orkeon.Domain.Tests.Composition;

/// <summary>
/// Tests for CompositionResult following Clean Architecture principles.
/// Tests the composition result record and its behavior.
/// </summary>
public class CompositionResultTests
{
    #region Test Helpers

    private static DomainAgent CreateTestAgent(string role)
    {
        return DomainAgent.Create(
            AgentRole.From(role),
            AgentGoal.From($"Goal for {role}"),
            AgentBackstory.From($"Backstory for {role}"));
    }

    private static DomainTask CreateTestTask(string description, string? agentId = null)
    {
        return DomainTask.Create(
            TaskDescription.From(description),
            ExpectedOutput.From($"Expected output for: {description}"));
    }

    private static DomainCrew CreateTestCrew(string name)
    {
        return DomainCrew.Create(
            $"Goal for {name}",
            ProcessType.Sequential);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingCompositionResultWithDefaultConstructor()
    {
        // Act
        var result = new CompositionResult();

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Crew);
        Assert.Empty(result.SelectedAgents);
        Assert.Empty(result.PlannedTasks);
        Assert.Empty(result.AgentAssignments);
        Assert.Equal(0.0, result.ConfidenceScore);
        Assert.Empty(result.Warnings);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCompositionResultUsingProperties()
    {
        // Arrange
        var crew = CreateTestCrew("Test Crew");
        var agents = new List<DomainAgent>
        {
            CreateTestAgent(RoleDeveloper),
            CreateTestAgent("Tester")
        };
        var tasks = new List<DomainTask>
        {
            CreateTestTask(GoalWriteCode),
            CreateTestTask("Test code")
        };
        var assignments = new Dictionary<string, string>
        {
            { tasks[0].Id, agents[0].Id },
            { tasks[1].Id, agents[1].Id }
        };
        var warnings = new List<string> { "Warning 1", "Warning 2" };

        // Act
        var result = new CompositionResult
        {
            Success = true,
            Crew = crew,
            SelectedAgents = agents,
            PlannedTasks = tasks,
            AgentAssignments = assignments,
            ConfidenceScore = 0.85,
            Warnings = warnings,
            Error = "Some error"
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(crew, result.Crew);
        Assert.Equal(agents, result.SelectedAgents);
        Assert.Equal(tasks, result.PlannedTasks);
        Assert.Equal(assignments, result.AgentAssignments);
        Assert.Equal(0.85, result.ConfidenceScore);
        Assert.Equal(warnings, result.Warnings);
        Assert.Equal("Some error", result.Error);
    }

    #endregion

    #region Success Scenarios Tests

    [Fact]
    public void ShouldHaveCorrectState_WhenUsingCompositionResultUsingSuccessfulComposition()
    {
        // Arrange
        var crew = CreateTestCrew("Development Team");
        var developer = CreateTestAgent(RoleSeniorDeveloper);
        var tester = CreateTestAgent("QA Engineer");
        var codeTask = CreateTestTask("Implement feature");
        var testTask = CreateTestTask("Test feature");

        // Act
        var result = new CompositionResult
        {
            Success = true,
            Crew = crew,
            SelectedAgents = [developer, tester],
            PlannedTasks = [codeTask, testTask],
            AgentAssignments = new Dictionary<string, string>
            {
                { codeTask.Id, developer.Id },
                { testTask.Id, tester.Id }
            },
            ConfidenceScore = 0.9,
            Warnings = [],
            Error = null
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.SelectedAgents.Count);
        Assert.Equal(2, result.PlannedTasks.Count);
        Assert.Equal(2, result.AgentAssignments.Count);
        Assert.Equal(0.9, result.ConfidenceScore);
        Assert.Empty(result.Warnings);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompositionResultUsingSuccessWithWarnings()
    {
        // Arrange & Act
        var result = new CompositionResult
        {
            Success = true,
            Crew = CreateTestCrew("Team with warnings"),
            SelectedAgents = [CreateTestAgent("Agent 1")],
            PlannedTasks = [CreateTestTask("Task 1")],
            ConfidenceScore = 0.7,
            Warnings =
            [
                "Agent may be overloaded",
                "Task complexity is high",
                "Consider adding more agents"
            ]
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Warnings.Count);
        Assert.Contains("Agent may be overloaded", result.Warnings);
        Assert.Null(result.Error);
    }

    #endregion

    #region Failure Scenarios Tests

    [Fact]
    public void ShouldHaveCorrectState_WhenUsingCompositionResultUsingFailedComposition()
    {
        // Arrange & Act
        var result = new CompositionResult
        {
            Success = false,
            Crew = null,
            Error = "Not enough agents available for the required tasks",
            ConfidenceScore = 0.0
        };

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Crew);
        Assert.Empty(result.SelectedAgents);
        Assert.Empty(result.PlannedTasks);
        Assert.Empty(result.AgentAssignments);
        Assert.Equal(0.0, result.ConfidenceScore);
        Assert.Equal("Not enough agents available for the required tasks", result.Error);
    }

    [Fact]
    public void ShouldContainPartialData_WhenUsingCompositionResultWithPartialFailure()
    {
        // Arrange & Act
        var result = new CompositionResult
        {
            Success = false,
            Crew = null,
            SelectedAgents = [CreateTestAgent("Available Agent")],
            PlannedTasks =
            [
                CreateTestTask("Task 1"),
                CreateTestTask("Task 2"),
                CreateTestTask("Task 3")
            ],
            Error = "Could not assign all tasks - insufficient agents",
            ConfidenceScore = 0.3,
            Warnings = ["Only 1 agent available for 3 tasks"]
        };

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Crew);
        Assert.Single(result.SelectedAgents);
        Assert.Equal(3, result.PlannedTasks.Count);
        Assert.Empty(result.AgentAssignments); // Failed to complete assignments
        Assert.Equal(0.3, result.ConfidenceScore);
        Assert.NotEmpty(result.Warnings);
    }

    #endregion

    #region AgentAssignments Tests

    [Fact]
    public void ShouldMapCorrectly_WhenUsingAgentAssignmentsWithValidAssignments()
    {
        // Arrange
        var agent1 = CreateTestAgent("Agent 1");
        var agent2 = CreateTestAgent("Agent 2");
        var task1 = CreateTestTask("Task 1");
        var task2 = CreateTestTask("Task 2");
        var task3 = CreateTestTask("Task 3");

        // Act
        var result = new CompositionResult
        {
            AgentAssignments = new Dictionary<string, string>
            {
                { task1.Id, agent1.Id },
                { task2.Id, agent1.Id }, // Same agent for multiple tasks
                { task3.Id, agent2.Id }
            }
        };

        // Assert
        Assert.Equal(3, result.AgentAssignments.Count);
        Assert.Equal(agent1.Id, result.AgentAssignments[task1.Id]);
        Assert.Equal(agent1.Id, result.AgentAssignments[task2.Id]);
        Assert.Equal(agent2.Id, result.AgentAssignments[task3.Id]);
    }

    [Fact]
    public void ShouldUpdate_WhenUsingWithExpressionToReassign()
    {
        // Arrange
        var taskId = TaskId.Create();
        var originalAgentId = AgentId.Create();
        var newAgentId = AgentId.Create();

        var result = new CompositionResult
        {
            AgentAssignments = new Dictionary<string, string> { { taskId, originalAgentId } }
        };

        // Act — use with expression for immutable update
        var updated = result with
        {
            AgentAssignments = new Dictionary<string, string> { { taskId, newAgentId } }
        };

        // Assert
        Assert.Single(updated.AgentAssignments);
        Assert.Equal(newAgentId, updated.AgentAssignments[taskId]);
    }

    #endregion

    #region ConfidenceScore Tests

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.99)]
    [InlineData(1.0)]
    public void ShouldBeAccepted_WhenUsingConfidenceScoreWithValidValues(double score)
    {
        // Arrange & Act
        var result = new CompositionResult { ConfidenceScore = score };

        // Assert
        Assert.Equal(score, result.ConfidenceScore);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(-10.0)]
    [InlineData(10.0)]
    public void ShouldBeAccepted_WhenUsingConfidenceScoreUsingOutOfRangeValues(double score)
    {
        // Arrange & Act
        var result = new CompositionResult { ConfidenceScore = score };

        // Assert
        Assert.Equal(score, result.ConfidenceScore);
        // Note: No validation in the record itself
    }

    #endregion

    #region Immutability Tests

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithExpression()
    {
        // Arrange
        var agent1 = CreateTestAgent("Agent 1");
        var agent2 = CreateTestAgent("Agent 2");
        var agent3 = CreateTestAgent("Agent 3");

        var result = new CompositionResult
        {
            SelectedAgents = [agent1, agent2]
        };

        // Act — with expression creates a new instance
        var updated = result with
        {
            SelectedAgents = [agent1, agent3]
        };

        // Assert
        Assert.Equal(2, result.SelectedAgents.Count);
        Assert.Equal(2, updated.SelectedAgents.Count);
        Assert.Contains(agent2, result.SelectedAgents);
        Assert.DoesNotContain(agent2, updated.SelectedAgents);
        Assert.Contains(agent3, updated.SelectedAgents);
    }

    [Fact]
    public void ShouldSupportWithExpression_WhenUsingPlannedTasks()
    {
        // Arrange
        var result = new CompositionResult
        {
            PlannedTasks =
            [
                CreateTestTask("Initial Task 1"),
                CreateTestTask("Initial Task 2")
            ]
        };

        // Act
        var updated = result with
        {
            PlannedTasks = [CreateTestTask("New Task")]
        };

        // Assert
        Assert.Equal(2, result.PlannedTasks.Count);
        Assert.Single(updated.PlannedTasks);
        Assert.Equal("New Task", updated.PlannedTasks[0].Description.Value);
    }

    [Fact]
    public void ShouldSupportDuplicates_WhenUsingWarnings()
    {
        // Arrange
        var warning = "Duplicate warning message";

        // Act
        var result = new CompositionResult
        {
            Warnings = [warning, warning, warning]
        };

        // Assert
        Assert.Equal(3, result.Warnings.Count);
        Assert.All(result.Warnings, w => Assert.Equal(warning, w));
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldCompleteTeamCompositionScenario_WhenUsingCompositionResult()
    {
        // Arrange - Simulate a complete team composition for a software project
        var projectCrew = CreateTestCrew("Software Development Project");

        var architect = CreateTestAgent("Solution Architect");
        var leadDev = CreateTestAgent("Lead Developer");
        var backendDev = CreateTestAgent("Backend Developer");
        var frontendDev = CreateTestAgent("Frontend Developer");
        var qaEngineer = CreateTestAgent("QA Engineer");
        var devOps = CreateTestAgent("DevOps Engineer");

        var designTask = CreateTestTask("Design system architecture");
        var apiTask = CreateTestTask("Develop REST API");
        var uiTask = CreateTestTask("Build user interface");
        var integrationTask = CreateTestTask("Integration testing");
        var deploymentTask = CreateTestTask("Setup CI/CD pipeline");

        // Act
        var result = new CompositionResult
        {
            Success = true,
            Crew = projectCrew,
            SelectedAgents =
            [
                architect, leadDev, backendDev, frontendDev, qaEngineer, devOps
            ],
            PlannedTasks =
            [
                designTask, apiTask, uiTask, integrationTask, deploymentTask
            ],
            AgentAssignments = new Dictionary<string, string>
            {
                { designTask.Id, architect.Id },
                { apiTask.Id, backendDev.Id },
                { uiTask.Id, frontendDev.Id },
                { integrationTask.Id, qaEngineer.Id },
                { deploymentTask.Id, devOps.Id }
            },
            ConfidenceScore = 0.95,
            Warnings =
            [
                "Consider adding a second backend developer for large API",
                "Frontend developer may need React expertise"
            ]
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(6, result.SelectedAgents.Count);
        Assert.Equal(5, result.PlannedTasks.Count);
        Assert.Equal(5, result.AgentAssignments.Count);
        Assert.Equal(0.95, result.ConfidenceScore);
        Assert.Equal(2, result.Warnings.Count);

        // Verify all tasks are assigned
        foreach (var task in result.PlannedTasks)
        {
            Assert.True(result.AgentAssignments.ContainsKey(task.Id));
        }
    }

    [Fact]
    public void ShouldInsufficientResourcesScenario_WhenUsingCompositionResult()
    {
        // Arrange - Many tasks but few agents available
        var tasks = Enumerable.Range(1, 10)
            .Select(i => CreateTestTask($"Task {i}"))
            .ToList();

        var agents = new List<DomainAgent>
        {
            CreateTestAgent("Overworked Agent 1"),
            CreateTestAgent("Overworked Agent 2")
        };

        // Act
        var result = new CompositionResult
        {
            Success = false,
            SelectedAgents = agents,
            PlannedTasks = tasks,
            ConfidenceScore = 0.2,
            Error = "Cannot effectively distribute 10 tasks among 2 agents",
            Warnings =
            [
                "Agent workload would exceed recommended limits",
                "Task completion time would be significantly delayed",
                "Quality may be compromised due to overload"
            ]
        };

        // Assert
        Assert.False(result.Success);
        Assert.Equal(2, result.SelectedAgents.Count);
        Assert.Equal(10, result.PlannedTasks.Count);
        Assert.Empty(result.AgentAssignments); // Failed to create assignments
        Assert.Equal(0.2, result.ConfidenceScore);
        Assert.Equal(3, result.Warnings.Count);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCompositionResultWithNullCollections()
    {
        // Arrange & Act
        var result = new CompositionResult
        {
            SelectedAgents = null!,
            PlannedTasks = null!,
            AgentAssignments = null!,
            Warnings = null!
        };

        // Assert
        Assert.Null(result.SelectedAgents);
        Assert.Null(result.PlannedTasks);
        Assert.Null(result.AgentAssignments);
        Assert.Null(result.Warnings);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingCompositionResultWithEmptyButSuccessful()
    {
        // Arrange & Act
        var result = new CompositionResult
        {
            Success = true,
            Crew = CreateTestCrew("Empty Crew"),
            ConfidenceScore = 1.0
        };

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.SelectedAgents);
        Assert.Empty(result.PlannedTasks);
        Assert.Empty(result.AgentAssignments);
        Assert.Equal(1.0, result.ConfidenceScore);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCompositionResultWithSpecialCharacters()
    {
        // Arrange & Act
        var result = new CompositionResult
        {
            Success = false,
            Error = "Failed to compose crew: Invalid character '>' in agent role",
            Warnings =
            [
                "Warning with unicode: ⚠️ Check configuration",
                "Special chars: <script>alert('test')</script>",
                "Multi-line\nwarning\nmessage"
            ]
        };

        // Assert
        Assert.Contains("'>'", result.Error);
        Assert.Contains("⚠️", result.Warnings[0]);
        Assert.Contains("<script>", result.Warnings[1]);
        Assert.Contains("\n", result.Warnings[2]);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingCompositionResultToString()
    {
        // Arrange
        var result = new CompositionResult
        {
            Success = true,
            ConfidenceScore = 0.85
        };

        // Act
        var stringRepresentation = result.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("CompositionResult", stringRepresentation);
    }

    #endregion
}
