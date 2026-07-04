using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Domain.Tests.ValueObjects;

public class AgentStepContextTests
{
    [Fact]
    public void ShouldCreateContext_WhenConstructingWithValidParameters()
    {
        // Arrange
        var thought = "I need to analyze the data";
        var action = "analyze";
        var actionInput = "sales_data.csv";
        var observation = "Data analyzed successfully";
        var toolCall = ToolCall.Create("DataAnalyzer",
            ToolArguments.CreateBuilder()
                .AddString("file", "sales_data.csv")
                .Build());
        var metadata = new StepMetadata(
            DateTime.UtcNow,
            TimeSpan.FromSeconds(2),
            150);

        // Act
        var context = new AgentStepContext(
            thought,
            action,
            actionInput,
            observation,
            toolCall,
            metadata);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal(action, context.Action);
        Assert.Equal(actionInput, context.ActionInput);
        Assert.Equal(observation, context.Observation);
        Assert.Equal(toolCall, context.ToolCall);
        Assert.Equal(metadata, context.Metadata);
    }

    [Fact]
    public void ShouldReturnEmptyContext_WhenUsingEmpty()
    {
        // Act
        var context = AgentStepContext.Empty;

        // Assert
        Assert.Equal(string.Empty, context.Thought);
        Assert.Equal(string.Empty, context.Action);
        Assert.Equal(string.Empty, context.ActionInput);
        Assert.Equal(string.Empty, context.Observation);
        Assert.Null(context.ToolCall);
        Assert.NotNull(context.Metadata);
        Assert.Equal(0, context.Metadata.TokensUsed);
    }

    [Fact]
    public void ShouldCreateThoughtContext_WhenUsingForThought()
    {
        // Arrange
        var thought = "I should first check the current status";

        // Act
        var context = AgentStepContext.ForThought(thought);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("think", context.Action);
        Assert.Equal(string.Empty, context.ActionInput);
        Assert.Equal(string.Empty, context.Observation);
        Assert.Null(context.ToolCall);
        Assert.NotNull(context.Metadata);
    }

    [Fact]
    public void ShouldUseProvidedMetadata_WhenUsingForThoughtWithMetadata()
    {
        // Arrange
        var thought = "Processing information";
        var metadata = new StepMetadata(
            DateTime.UtcNow,
            TimeSpan.FromSeconds(1),
            100,
            null,
            ModelGpt4,
            0.7);

        // Act
        var context = AgentStepContext.ForThought(thought, metadata);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("think", context.Action);
        Assert.Equal(metadata, context.Metadata);
        Assert.Equal(ModelGpt4, context.Metadata.Model);
        Assert.Equal(0.7, context.Metadata.Temperature);
    }

    [Fact]
    public void ShouldCreateToolExecutionContext_WhenUsingForToolExecution()
    {
        // Arrange
        var thought = "I'll use the calculator to compute this";
        var toolCall = ToolCall.Create("Calculator",
            ToolArguments.CreateBuilder()
                .AddString("expression", "2+2")
                .Build());
        var observation = "Result: 4";

        // Act
        var context = AgentStepContext.ForToolExecution(thought, toolCall, observation);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("tool", context.Action);
        Assert.Equal("Calculator", context.ActionInput);
        Assert.Equal(observation, context.Observation);
        Assert.Equal(toolCall, context.ToolCall);
        Assert.NotNull(context.Metadata);
    }

    [Fact]
    public void ShouldUseProvidedMetadata_WhenUsingForToolExecutionWithMetadata()
    {
        // Arrange
        var thought = "Using file reader";
        var toolCall = ToolCall.Create("FileReader",
            ToolArguments.CreateBuilder()
                .AddString(ParamPath, "/data/file.txt")
                .Build());
        var observation = "File contents: Hello World";
        var metadata = new StepMetadata(
            DateTime.UtcNow,
            TimeSpan.FromMilliseconds(500),
            50);

        // Act
        var context = AgentStepContext.ForToolExecution(thought, toolCall, observation, metadata);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("tool", context.Action);
        Assert.Equal("FileReader", context.ActionInput);
        Assert.Equal(observation, context.Observation);
        Assert.Equal(toolCall, context.ToolCall);
        Assert.Equal(metadata, context.Metadata);
    }

    [Fact]
    public void ShouldCreateDelegationContext_WhenUsingForDelegation()
    {
        // Arrange
        var thought = "This task requires specialized knowledge";
        var delegateToAgent = "DataAnalyst";
        var taskDescription = "Analyze sales trends for Q4";
        var observation = "Task delegated successfully";

        // Act
        var context = AgentStepContext.ForDelegation(
            thought,
            delegateToAgent,
            taskDescription,
            observation);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("delegate", context.Action);
        Assert.Equal("DataAnalyst: Analyze sales trends for Q4", context.ActionInput);
        Assert.Equal(observation, context.Observation);
        Assert.Null(context.ToolCall);
        Assert.NotNull(context.Metadata);
    }

    [Fact]
    public void ShouldUseProvidedMetadata_WhenUsingForDelegationWithMetadata()
    {
        // Arrange
        var thought = "Delegating to expert";
        var delegateToAgent = "SecurityExpert";
        var taskDescription = "Review security configurations";
        var observation = "Delegation accepted";
        var metadata = new StepMetadata(
            DateTime.UtcNow,
            TimeSpan.FromSeconds(3),
            200,
            null,
            ModelGpt35Turbo);

        // Act
        var context = AgentStepContext.ForDelegation(
            thought,
            delegateToAgent,
            taskDescription,
            observation,
            metadata);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("delegate", context.Action);
        Assert.Equal("SecurityExpert: Review security configurations", context.ActionInput);
        Assert.Equal(observation, context.Observation);
        Assert.Equal(metadata, context.Metadata);
    }

    [Fact]
    public void ShouldCreateFinalAnswerContext_WhenUsingForFinalAnswer()
    {
        // Arrange
        var thought = "Based on my analysis, I can provide the answer";
        var answer = "The total revenue for Q4 was $1.2M with a 15% growth";

        // Act
        var context = AgentStepContext.ForFinalAnswer(thought, answer);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("final_answer", context.Action);
        Assert.Equal(answer, context.ActionInput);
        Assert.Equal(string.Empty, context.Observation);
        Assert.Null(context.ToolCall);
        Assert.NotNull(context.Metadata);
    }

    [Fact]
    public void ShouldUseProvidedMetadata_WhenUsingForFinalAnswerWithMetadata()
    {
        // Arrange
        var thought = "Conclusion reached";
        var answer = "Task completed successfully";
        var metadata = new StepMetadata(
            DateTime.UtcNow,
            TimeSpan.FromSeconds(5),
            300,
            null,
            "claude-2",
            0.5);

        // Act
        var context = AgentStepContext.ForFinalAnswer(thought, answer, metadata);

        // Assert
        Assert.Equal(thought, context.Thought);
        Assert.Equal("final_answer", context.Action);
        Assert.Equal(answer, context.ActionInput);
        Assert.Equal(metadata, context.Metadata);
        Assert.Equal("claude-2", context.Metadata.Model);
        Assert.Equal(0.5, context.Metadata.Temperature);
    }

    [Fact]
    public void ShouldSupportEquality_WhenUsingAgentStepContextUsingAsRecord()
    {
        // Arrange
        var metadata = new StepMetadata(
            DateTime.Parse("2024-01-01T00:00:00Z"),
            TimeSpan.FromSeconds(1),
            100);

        var context1 = new AgentStepContext(
            "thought",
            "action",
            "input",
            "observation",
            null,
            metadata);

        var context2 = new AgentStepContext(
            "thought",
            "action",
            "input",
            "observation",
            null,
            metadata);

        var context3 = new AgentStepContext(
            "different thought",
            "action",
            "input",
            "observation",
            null,
            metadata);

        // Act & Assert
        Assert.Equal(context1, context2);
        Assert.NotEqual(context1, context3);
        Assert.Equal(context1.GetHashCode(), context2.GetHashCode());
    }

    [Fact]
    public void ShouldInitialize_WhenUsingStepMetadataUsingConstructorWithAllParameters()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var duration = TimeSpan.FromSeconds(2.5);
        var tokensUsed = 150;
        var error = "Rate limit exceeded";
        var model = ModelGpt4;
        var temperature = 0.8;

        // Act
        var metadata = new StepMetadata(
            timestamp,
            duration,
            tokensUsed,
            error,
            model,
            temperature);

        // Assert
        Assert.Equal(timestamp, metadata.Timestamp);
        Assert.Equal(duration, metadata.Duration);
        Assert.Equal(tokensUsed, metadata.TokensUsed);
        Assert.Equal(error, metadata.Error);
        Assert.Equal(model, metadata.Model);
        Assert.Equal(temperature, metadata.Temperature);
    }

    [Fact]
    public void ShouldInitialize_WhenUsingStepMetadataUsingConstructorWithRequiredParameters()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var duration = TimeSpan.FromSeconds(1);
        var tokensUsed = 50;

        // Act
        var metadata = new StepMetadata(timestamp, duration, tokensUsed);

        // Assert
        Assert.Equal(timestamp, metadata.Timestamp);
        Assert.Equal(duration, metadata.Duration);
        Assert.Equal(tokensUsed, metadata.TokensUsed);
        Assert.Null(metadata.Error);
        Assert.Null(metadata.Model);
        Assert.Null(metadata.Temperature);
    }

    [Fact]
    public void ShouldReturnDefaultMetadata_WhenUsingStepMetadataWithEmpty()
    {
        // Act
        var metadata = StepMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.True(metadata.Timestamp <= DateTime.UtcNow);
        Assert.Equal(TimeSpan.Zero, metadata.Duration);
        Assert.Equal(0, metadata.TokensUsed);
        Assert.Null(metadata.Error);
        Assert.Null(metadata.Model);
        Assert.Null(metadata.Temperature);
    }

    [Fact]
    public void ShouldSupportEquality_WhenUsingStepMetadataUsingAsRecord()
    {
        // Arrange
        var timestamp = DateTime.Parse("2024-01-01T00:00:00Z");
        var duration = TimeSpan.FromSeconds(1);

        var metadata1 = new StepMetadata(timestamp, duration, 100, null, ModelGpt4, 0.7);
        var metadata2 = new StepMetadata(timestamp, duration, 100, null, ModelGpt4, 0.7);
        var metadata3 = new StepMetadata(timestamp, duration, 200, null, ModelGpt4, 0.7);

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.NotEqual(metadata1, metadata3);
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }
}
