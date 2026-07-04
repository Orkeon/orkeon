using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Planning;
using Orkeon.Application.Agent;
using ApplicationPlanningConfiguration = Orkeon.Application.Agent.PlanningConfiguration;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services;

public class AgentPlannerTypesTests
{
    #region PlanningConfiguration Tests

    [Fact]
    public void ShouldUseCorrectValues_WhenUsingPlanningConfigurationWithDefaults()
    {
        // Act
        var config = new ApplicationPlanningConfiguration();

        // Assert
        Assert.True(config.EnablePlanning);
        Assert.Equal(3, config.MaxRetries);
        Assert.Equal(TimeoutStandard, config.Timeout);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingPlanningConfigurationWithCustomValues()
    {
        // Arrange & Act
        var config = new ApplicationPlanningConfiguration
        {
            EnablePlanning = false,
            MaxRetries = 5,
            Timeout = TimeoutExtended
        };

        // Assert
        Assert.False(config.EnablePlanning);
        Assert.Equal(5, config.MaxRetries);
        Assert.Equal(TimeoutExtended, config.Timeout);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingPlanningConfigurationUsingEquality()
    {
        // Arrange
        var config1 = new ApplicationPlanningConfiguration
        {
            EnablePlanning = true,
            MaxRetries = 3,
            Timeout = TimeoutStandard
        };
        var config2 = new ApplicationPlanningConfiguration
        {
            EnablePlanning = true,
            MaxRetries = 3,
            Timeout = TimeoutStandard
        };
        var config3 = new ApplicationPlanningConfiguration
        {
            EnablePlanning = false,
            MaxRetries = 3,
            Timeout = TimeoutStandard
        };

        // Act & Assert
        Assert.Equal(config1, config2);
        Assert.NotEqual(config1, config3);
        Assert.Equal(config1.GetHashCode(), config2.GetHashCode());
        Assert.NotEqual(config1.GetHashCode(), config3.GetHashCode());
    }

    #endregion

    #region PlanningContext Tests

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingPlanningContextWithValidParameters()
    {
        // Arrange
        var taskDescription = GoalAnalyzeData;
        var expectedOutput = "Data analysis report";
        var tools = new List<string> { "Excel", "Python" };
        var agentRole = RoleDataAnalyst;
        var agentBackstory = "Expert in data analysis";
        var additionalContext = "Use statistical methods";

        // Act
        var context = new PlanningContext(
            taskDescription,
            expectedOutput,
            tools,
            agentRole,
            agentBackstory,
            additionalContext);

        // Assert
        Assert.Equal(taskDescription, context.TaskDescription);
        Assert.Equal(expectedOutput, context.ExpectedOutput);
        Assert.Equal(tools, context.Tools);
        Assert.Equal(agentRole, context.AgentRole);
        Assert.Equal(agentBackstory, context.AgentBackstory);
        Assert.Equal(additionalContext, context.AdditionalContext);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPlanningContextWithNullAdditionalContext()
    {
        // Arrange & Act
        var context = new PlanningContext(
            "Task",
            "Output",
            [],
            "Role",
            "Backstory",
            null);

        // Assert
        Assert.Null(context.AdditionalContext);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPlanningContextWithEmptyTools()
    {
        // Arrange & Act
        var context = new PlanningContext(
            "Task",
            "Output",
            [],
            "Role",
            "Backstory",
            "Context");

        // Assert
        Assert.Empty(context.Tools);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingPlanningContextUsingEquality()
    {
        // Arrange
        var tools = new List<string> { "Tool1", "Tool2" };
        var context1 = new PlanningContext("Task", "Output", tools, "Role", "Backstory", "Context");
        var context2 = new PlanningContext("Task", "Output", tools, "Role", "Backstory", "Context");
        var context3 = new PlanningContext("Different", "Output", tools, "Role", "Backstory", "Context");

        // Act & Assert
        Assert.Equal(context1, context2);
        Assert.NotEqual(context1, context3);
    }

    #endregion

    #region PlanningStep Tests

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingPlanningStepWithValidParameters()
    {
        // Arrange
        var stepNumber = 1;
        var title = "Data Collection";
        var description = "Collect all necessary data";
        var actions = new List<string> { "Connect to database", "Extract data", "Validate data" };

        // Act
        var step = new PlanningStep(stepNumber, title, description, actions);

        // Assert
        Assert.Equal(stepNumber, step.StepNumber);
        Assert.Equal(title, step.Title);
        Assert.Equal(description, step.Description);
        Assert.Equal(actions, step.Actions);
        Assert.Equal(3, step.Actions.Count);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPlanningStepWithEmptyActions()
    {
        // Arrange & Act
        var step = new PlanningStep(1, "Title", "Description", []);

        // Assert
        Assert.Empty(step.Actions);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingPlanningStepUsingEquality()
    {
        // Arrange
        var actions = new List<string> { "Action1", "Action2" };
        var step1 = new PlanningStep(1, "Title", "Description", actions);
        var step2 = new PlanningStep(1, "Title", "Description", actions);
        var step3 = new PlanningStep(2, "Title", "Description", actions);

        // Act & Assert
        Assert.Equal(step1, step2);
        Assert.NotEqual(step1, step3);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPlanningStepWithNegativeStepNumber()
    {
        // Arrange & Act
        var step = new PlanningStep(-1, "Title", "Description", []);

        // Assert
        Assert.Equal(-1, step.StepNumber);
    }

    #endregion

    #region PlanningResult Tests

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingPlanningResultWithValidParameters()
    {
        // Arrange
        var taskPlans = new List<TaskPlan>
        {
            new TaskPlan { Id = TaskPlanId.Create(), TaskId = TaskId.Create(), EstimatedDuration = TimeSpan.FromMinutes(30) },
            new TaskPlan { Id = TaskPlanId.Create(), TaskId = TaskId.Create(), EstimatedDuration = TimeSpan.FromMinutes(45) }
        }.AsReadOnly();
        var planningDuration = TimeSpan.FromSeconds(120);
        var success = true;

        // Act
        var result = new PlanningResult(taskPlans, planningDuration, success);

        // Assert
        Assert.Equal(taskPlans, result.TaskPlans);
        Assert.Equal(planningDuration, result.PlanningDuration);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskPlans.Count);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPlanningResultWithEmptyTaskPlans()
    {
        // Arrange & Act
        var result = new PlanningResult(
            new List<TaskPlan>().AsReadOnly(),
            TimeSpan.FromSeconds(10),
            false);

        // Assert
        Assert.Empty(result.TaskPlans);
        Assert.False(result.Success);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPlanningResultWithZeroDuration()
    {
        // Arrange & Act
        var result = new PlanningResult(
            new List<TaskPlan>().AsReadOnly(),
            TimeSpan.Zero,
            true);

        // Assert
        Assert.Equal(TimeSpan.Zero, result.PlanningDuration);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingPlanningResultUsingEquality()
    {
        // Arrange
        var taskPlans = new List<TaskPlan>
        {
            new TaskPlan { Id = TaskPlanId.Create(), TaskId = TaskId.Create(), EstimatedDuration = TimeoutExtended }
        }.AsReadOnly();
        var duration = TimeSpan.FromSeconds(60);

        var result1 = new PlanningResult(taskPlans, duration, true);
        var result2 = new PlanningResult(taskPlans, duration, true);
        var result3 = new PlanningResult(taskPlans, duration, false);

        // Act & Assert
        Assert.Equal(result1, result2);
        Assert.NotEqual(result1, result3);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingPlanningResultWithNegativeDuration()
    {
        // Arrange & Act
        var result = new PlanningResult(
            new List<TaskPlan>().AsReadOnly(),
            TimeSpan.FromSeconds(-10),
            false);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(-10), result.PlanningDuration);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ShouldWorkTogether_WhenUsingPlanningWorkflowWithAllTypes()
    {
        // Arrange
        var config = new ApplicationPlanningConfiguration
        {
            EnablePlanning = true,
            MaxRetries = 2,
            Timeout = TimeSpan.FromMinutes(3)
        };

        var context = new PlanningContext(
            "Complex analysis task",
            "Detailed report",
            ["DataTool", "AnalysisTool"],
            "Senior Analyst",
            "10 years experience",
            "Focus on accuracy");

        var steps = new List<PlanningStep>
        {
            new PlanningStep(1, "Preparation", "Prepare for analysis",
                ["Setup tools", "Load data"]),
            new PlanningStep(2, "Analysis", "Perform analysis",
                ["Run calculations", "Generate charts"]),
            new PlanningStep(3, "Reporting", "Create report",
                ["Write summary", "Export results"])
        };

        var taskPlans = steps.Select(s =>
            new TaskPlan { Id = TaskPlanId.Create(), TaskId = TaskId.Create(), EstimatedDuration = TimeoutLong }).ToList().AsReadOnly();

        var result = new PlanningResult(taskPlans, TimeSpan.FromMinutes(2), true);

        // Act & Assert
        Assert.True(config.EnablePlanning);
        Assert.Equal("Complex analysis task", context.TaskDescription);
        Assert.Equal(3, steps.Count);
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskPlans.Count);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingPlanningTypesDeepCopy()
    {
        // Arrange
        var originalTools = new List<string> { "Tool1", "Tool2" };
        var originalActions = new List<string> { "Action1", "Action2" };

        // Create records with copies of the lists to ensure immutability
        var context = new PlanningContext(
            "Task", "Output", [.. originalTools], "Role", "Backstory", "Context");
        var step = new PlanningStep(1, "Title", "Description", [.. originalActions]);

        // Act - Modify original lists
        originalTools.Add("Tool3");
        originalActions.Add("Action3");

        // Assert - Records should not be affected by list modifications when created with copies
        Assert.Equal(2, context.Tools.Count);
        Assert.Equal(2, step.Actions.Count);
        Assert.DoesNotContain("Tool3", context.Tools);
        Assert.DoesNotContain("Action3", step.Actions);
    }

    #endregion
}
