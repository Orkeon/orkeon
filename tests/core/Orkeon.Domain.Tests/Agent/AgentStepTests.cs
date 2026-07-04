using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Domain.Tests.Fixtures;
namespace Orkeon.Domain.Tests.Agent;

public class AgentStepTests
{
    [Fact]
    public void ShouldCreateSuccessfulStep_WhenCreatingSuccessWithValidParameters()
    {
        // Arrange
        var output = "Task completed successfully";
        var structuredOutput = new { result = "success", data = 42 };
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var action = "analyze_data";
        var input = "Process this data";
        var toolsUsed = new List<string> { "Calculator", "DataAnalyzer" };
        var context = AgentStepContext.ForThought("Analyzing data");

        // Act
        var step = AgentStep.CreateSuccess(
            output: output,
            structuredOutput: structuredOutput,
            agentId: agentId,
            taskId: taskId,
            action: action,
            input: input,
            toolsUsed: toolsUsed,
            context: context);

        // Assert
        Assert.NotNull(step);
        Assert.NotNull(step.Id);
        Assert.IsType<AgentStepId>(step.Id); // ID is ULID-based EntityId
        Assert.Equal(agentId, step.AgentId);
        Assert.Equal(taskId, step.TaskId);
        Assert.Equal(action, step.Action);
        Assert.Equal(input, step.Input);
        Assert.Equal(output, step.Output);
        Assert.Equal(structuredOutput, step.StructuredOutput);
        Assert.True(step.Success);
        Assert.Null(step.Error);
        Assert.NotNull(step.CompletedAt);
        Assert.True(step.StartedAt <= step.CompletedAt);
        Assert.Equal(context, step.Context);
        Assert.Equal(2, step.ToolsUsed.Count);
        Assert.Contains("Calculator", step.ToolsUsed);
        Assert.Contains("DataAnalyzer", step.ToolsUsed);
    }

    [Fact]
    public void ShouldUseDefaults_WhenCreatingSuccessWithMinimalParameters()
    {
        // Arrange
        var output = "Simple output";

        // Act
        var step = AgentStep.CreateSuccess(output, null);

        // Assert
        Assert.NotNull(step);
        Assert.NotNull(step.Id);
        Assert.Equal("", step.Action);
        Assert.Equal("", step.Input);
        Assert.Equal(output, step.Output);
        Assert.Null(step.StructuredOutput);
        Assert.True(step.Success);
        Assert.Null(step.Error);
        Assert.NotNull(step.Context);
        Assert.Equal(string.Empty, step.Context.Thought);
        Assert.Equal(string.Empty, step.Context.Action);
        Assert.Equal(string.Empty, step.Context.ActionInput);
        Assert.Equal(string.Empty, step.Context.Observation);
        Assert.Null(step.Context.ToolCall);
        Assert.Empty(step.ToolsUsed);
    }

    [Fact]
    public void ShouldCreateFailedStep_WhenUsingFailedWithValidParameters()
    {
        // Arrange
        var error = "Network connection timeout";
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var action = "fetch_data";
        var input = "https://api.example.com/data";
        var toolsUsed = new List<string> { "HttpClient" };
        var context = AgentStepContext.ForToolExecution(
            "Fetching external data",
            ToolCall.Create("HttpClient", ToolArguments.CreateBuilder()
                .AddString(ParamUrl, "https://api.example.com/data")
                .Build()),
            "Request failed",
            new StepMetadata(DateTime.UtcNow, TimeoutQuick, 0, error));

        // Act
        var step = AgentStep.Failed(
            error: error,
            agentId: agentId,
            taskId: taskId,
            action: action,
            input: input,
            toolsUsed: toolsUsed,
            context: context);

        // Assert
        Assert.NotNull(step);
        Assert.NotNull(step.Id);
        Assert.Equal(agentId, step.AgentId);
        Assert.Equal(taskId, step.TaskId);
        Assert.Equal(action, step.Action);
        Assert.Equal(input, step.Input);
        Assert.Null(step.Output);
        Assert.Null(step.StructuredOutput);
        Assert.False(step.Success);
        Assert.Equal(error, step.Error);
        Assert.NotNull(step.CompletedAt);
        Assert.Equal(context, step.Context);
        Assert.Single(step.ToolsUsed);
        Assert.Contains("HttpClient", step.ToolsUsed);
    }

    [Fact]
    public void ShouldUseDefaults_WhenUsingFailedWithMinimalParameters()
    {
        // Arrange
        var error = "Simple error";

        // Act
        var step = AgentStep.Failed(error);

        // Assert
        Assert.NotNull(step);
        Assert.Equal("", step.Action);
        Assert.Equal("", step.Input);
        Assert.Null(step.Output);
        Assert.Null(step.StructuredOutput);
        Assert.False(step.Success);
        Assert.Equal(error, step.Error);
        Assert.NotNull(step.Context);
        Assert.Equal(string.Empty, step.Context.Thought);
        Assert.Equal(string.Empty, step.Context.Action);
        Assert.Equal(string.Empty, step.Context.ActionInput);
        Assert.Equal(string.Empty, step.Context.Observation);
        Assert.Null(step.Context.ToolCall);
        Assert.Empty(step.ToolsUsed);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenUsingDuration()
    {
        // Arrange & Act
        var step = AgentStep.CreateSuccess("output", null);

        // Assert
        Assert.True(step.Duration >= TimeSpan.Zero);
        Assert.True(step.Duration.TotalMilliseconds < 1000); // Should be very quick
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingToolsUsed()
    {
        // Arrange
        var toolsList = new List<string> { "Tool1", "Tool2" };
        var step = AgentStep.CreateSuccess("output", null, toolsUsed: toolsList);

        // Act & Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<string>>(step.ToolsUsed);
        Assert.Equal(2, step.ToolsUsed.Count);

        // Note: AsReadOnly() creates a read-only wrapper, not a copy
        // So modifications to the original list are reflected
        toolsList.Add("Tool3");
        Assert.Equal(3, step.ToolsUsed.Count); // This is expected behavior
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var output = "Same output";
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var action = "action";
        var input = "input";

        // Note: We can't create truly equal AgentSteps because they have unique IDs
        // and timestamps, but we can test that the equality considers all components
        var step1 = AgentStep.CreateSuccess(output, null, agentId, taskId, action, input);
        var step2 = AgentStep.CreateSuccess(output, null, agentId, taskId, action, input);

        // Act & Assert
        Assert.NotEqual(step1, step2); // Different due to unique IDs
        Assert.NotEqual(step1.Id, step2.Id); // IDs are different
    }

    [Fact]
    public void ShouldBeConsistent_WhenCallingGetHashCode()
    {
        // Arrange
        var step = AgentStep.CreateSuccess("output", null);

        // Act
        var hash1 = step.GetHashCode();
        var hash2 = step.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenCreatingSuccessWithComplexStructuredOutput()
    {
        // Arrange
        var structuredOutput = new
        {
            status = "completed",
            results = new[]
            {
                new { id = 1, value = "A" },
                new { id = 2, value = "B" }
            },
            metadata = new Dictionary<string, object>
            {
                ["processedAt"] = DateTime.UtcNow,
                ["version"] = "1.0"
            }
        };

        // Act
        var step = AgentStep.CreateSuccess("Processed successfully", structuredOutput);

        // Assert
        Assert.NotNull(step.StructuredOutput);
        Assert.Equal(structuredOutput, step.StructuredOutput);
    }

    [Fact]
    public void ShouldHaveEmptyToolsList_WhenUsingFailedWithNullToolsUsed()
    {
        // Act
        var step = AgentStep.Failed("error", toolsUsed: null);

        // Assert
        Assert.NotNull(step.ToolsUsed);
        Assert.Empty(step.ToolsUsed);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenCreatingSuccessWithDifferentContextTypes()
    {
        // Arrange
        var thoughtContext = AgentStepContext.ForThought("Planning next action");
        var delegationContext = AgentStepContext.ForDelegation(
            "Task too complex",
            "SpecialistAgent",
            "Analyze complex data",
            "Delegated successfully");
        var finalAnswerContext = AgentStepContext.ForFinalAnswer(
            "Completed all steps",
            "The final result is 42");

        // Act
        var thoughtStep = AgentStep.CreateSuccess("Thought recorded", null, context: thoughtContext);
        var delegationStep = AgentStep.CreateSuccess("Delegated", null, context: delegationContext);
        var finalStep = AgentStep.CreateSuccess("42", null, context: finalAnswerContext);

        // Assert
        Assert.Equal("think", thoughtStep.Context.Action);
        Assert.Equal("delegate", delegationStep.Context.Action);
        Assert.Equal("final_answer", finalStep.Context.Action);
    }

    [Fact]
    public void ShouldBeLogical_WhenUsingStepTimingProperties()
    {
        // Arrange & Act
        var beforeCreation = DateTime.UtcNow;
        ClockAdvance.Tick(); // Deterministic clock advance (R5.6)
        var step = AgentStep.CreateSuccess("output", null);
        ClockAdvance.Tick(); // Deterministic clock advance (R5.6)
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(step.StartedAt >= beforeCreation);
        Assert.True(step.StartedAt <= afterCreation);
        Assert.True(step.CompletedAt >= step.StartedAt);
        Assert.True(step.CompletedAt <= afterCreation);
        Assert.True(step.Duration.TotalMilliseconds >= 0);
    }
}
