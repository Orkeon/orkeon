using System.Collections.Immutable;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.ValueObjects;

public class TypedTaskExecutionContextTests
{
    private static readonly string[] TransformationRules =
    [
        "normalize_phone_numbers",
        "standardize_addresses",
        "validate_email_formats",
        "calculate_derived_fields"
    ];

    #region Create Factory Method Tests

    [Fact]
    public void ShouldCreateContext_WhenCreatingWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        var context = TypedTaskExecutionContext.Create(taskId, agentId);

        // Assert
        Assert.Equal(taskId, context.TaskId);
        Assert.Equal(agentId, context.ExecutingAgent);
        Assert.Same(TaskVariables.Empty, context.Variables);
        Assert.True(context.PreviousOutputs.IsEmpty);
        Assert.NotNull(context.Metadata);
        Assert.True(context.ToolCalls.IsEmpty);
        Assert.False(context.Metadata.IsComplete);
        Assert.Null(context.Metadata.MaxExecutionTime);
    }

    [Fact]
    public void ShouldSetMaxExecutionTime_WhenCreatingWithMaxExecutionTime()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var maxTime = TimeoutExtended;

        // Act
        var context = TypedTaskExecutionContext.Create(taskId, agentId, maxTime);

        // Assert
        Assert.Equal(taskId, context.TaskId);
        Assert.Equal(agentId, context.ExecutingAgent);
        Assert.Equal(maxTime, context.Metadata.MaxExecutionTime);
        Assert.False(context.Metadata.IsComplete);
    }

    [Fact]
    public void ShouldGenerateUniqueExecutionId_WhenCreating()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        var context1 = TypedTaskExecutionContext.Create(taskId, agentId);
        var context2 = TypedTaskExecutionContext.Create(taskId, agentId);

        // Assert
        Assert.NotEqual(context1.Metadata.ExecutionId, context2.Metadata.ExecutionId);
        Assert.True(Guid.TryParse(context1.Metadata.ExecutionId, out _));
        Assert.True(Guid.TryParse(context2.Metadata.ExecutionId, out _));
    }

    [Fact]
    public void ShouldSetStartedAtToCurrentTime_WhenCreating()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var before = DateTime.UtcNow;

        // Act
        var context = TypedTaskExecutionContext.Create(taskId, agentId);

        // Assert
        var after = DateTime.UtcNow;
        Assert.True(context.Metadata.StartedAt >= before);
        Assert.True(context.Metadata.StartedAt <= after);
    }

    #endregion

    #region WithVariable Method Tests

    [Fact]
    public void ShouldAddVariable_WhenUsingWithVariableWithStringValue()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var context = TypedTaskExecutionContext.Create(taskId, agentId);

        // Act
        var updated = context.WithVariable("input", "test data");

        // Assert
        Assert.NotSame(context, updated);
        Assert.Null(context.Variables.GetString("input"));
        Assert.Equal("test data", updated.Variables.GetString("input"));
        Assert.Equal(taskId, updated.TaskId);
        Assert.Equal(agentId, updated.ExecutingAgent);
    }

    [Fact]
    public void ShouldAddVariable_WhenUsingWithVariableWithIntValue()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));

        // Act
        var updated = context.WithVariable("count", 42);

        // Assert
        Assert.Equal(42, updated.Variables.GetInt("count"));
    }

    [Fact]
    public void ShouldAddVariable_WhenUsingWithVariableWithBoolValue()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));

        // Act
        var updated = context.WithVariable("enabled", true);

        // Assert
        Assert.True(updated.Variables.GetBool("enabled"));
    }

    [Fact]
    public void ShouldAddVariable_WhenUsingWithVariableWithObjectValue()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var config = new { timeout = 30, retries = 3 };

        // Act
        var updated = context.WithVariable("config", config);

        // Assert
        Assert.Same(config, updated.Variables.GetObject<object>("config"));
    }

    [Fact]
    public void ShouldAddAllVariables_WhenUsingWithVariableWithMultipleVariables()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));

        // Act
        var updated = context
            .WithVariable("name", "test")
            .WithVariable("count", 100)
            .WithVariable("enabled", false);

        // Assert
        Assert.Equal("test", updated.Variables.GetString("name"));
        Assert.Equal(100, updated.Variables.GetInt("count"));
        Assert.False(updated.Variables.GetBool("enabled"));
        Assert.Equal(3, updated.Variables.Keys.Count());
    }

    #endregion

    #region WithPreviousOutput Method Tests

    [Fact]
    public void ShouldAddOutput_WhenUsingWithPreviousOutputWithSingleOutput()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var output = TaskOutput.Text("Previous task completed");

        // Act
        var updated = context.WithPreviousOutput(output);

        // Assert
        Assert.NotSame(context, updated);
        Assert.True(context.PreviousOutputs.IsEmpty);
        Assert.Single(updated.PreviousOutputs);
        Assert.Same(output, updated.PreviousOutputs[0]);
    }

    [Fact]
    public void ShouldAddAllOutputs_WhenUsingWithPreviousOutputWithMultipleOutputs()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var output1 = TaskOutput.Text("First output");
        var output2 = TaskOutput.Json("{\"result\": \"success\"}");
        var output3 = TaskOutput.Markdown("# Final Report");

        // Act
        var updated = context
            .WithPreviousOutput(output1)
            .WithPreviousOutput(output2)
            .WithPreviousOutput(output3);

        // Assert
        Assert.Equal(3, updated.PreviousOutputs.Length);
        Assert.Same(output1, updated.PreviousOutputs[0]);
        Assert.Same(output2, updated.PreviousOutputs[1]);
        Assert.Same(output3, updated.PreviousOutputs[2]);
    }

    [Fact]
    public void ShouldPreserveOrder_WhenUsingWithPreviousOutput()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var outputs = Enumerable.Range(1, 5)
            .Select(i => TaskOutput.Text($"Output {i}"))
            .ToArray();

        // Act
        var updated = context;
        foreach (var output in outputs)
        {
            updated = updated.WithPreviousOutput(output);
        }

        // Assert
        Assert.Equal(5, updated.PreviousOutputs.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Same(outputs[i], updated.PreviousOutputs[i]);
        }
    }

    #endregion

    #region WithToolCall Method Tests

    [Fact]
    public void ShouldAddToolCall_WhenUsingWithToolCallWithSingleToolCall()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var toolCall = ToolCall.Create("FileReader", ToolArguments.CreateBuilder()
            .AddString(ParamPath, "/data/file.txt")
            .Build());

        // Act
        var updated = context.WithToolCall(toolCall);

        // Assert
        Assert.NotSame(context, updated);
        Assert.True(context.ToolCalls.IsEmpty);
        Assert.Single(updated.ToolCalls);
        Assert.Same(toolCall, updated.ToolCalls[0]);
    }

    [Fact]
    public void ShouldAddAllToolCalls_WhenUsingWithToolCallWithMultipleToolCalls()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var toolCall1 = ToolCall.Create("FileReader", ToolArguments.Empty);
        var toolCall2 = ToolCall.Create("DataProcessor", ToolArguments.CreateBuilder()
            .AddInt("batchSize", 100)
            .Build());
        var toolCall3 = ToolCall.Create("ReportGenerator", ToolArguments.CreateBuilder()
            .AddString("format", "pdf")
            .Build());

        // Act
        var updated = context
            .WithToolCall(toolCall1)
            .WithToolCall(toolCall2)
            .WithToolCall(toolCall3);

        // Assert
        Assert.Equal(3, updated.ToolCalls.Length);
        Assert.Same(toolCall1, updated.ToolCalls[0]);
        Assert.Same(toolCall2, updated.ToolCalls[1]);
        Assert.Same(toolCall3, updated.ToolCalls[2]);
    }

    [Fact]
    public void ShouldPreserveOrder_WhenUsingWithToolCall()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var toolCalls = Enumerable.Range(1, 4)
            .Select(i => ToolCall.Create($"Tool{i}", ToolArguments.Empty))
            .ToArray();

        // Act
        var updated = context;
        foreach (var toolCall in toolCalls)
        {
            updated = updated.WithToolCall(toolCall);
        }

        // Assert
        Assert.Equal(4, updated.ToolCalls.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Same(toolCalls[i], updated.ToolCalls[i]);
        }
    }

    #endregion

    #region Complete Method Tests

    [Fact]
    public void ShouldMarkAsComplete_WhenCompletingWithoutError()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));

        // Act
        var completed = context.Complete();

        // Assert
        Assert.NotSame(context, completed);
        Assert.False(context.Metadata.IsComplete);
        Assert.True(completed.Metadata.IsComplete);
        Assert.NotNull(completed.Metadata.CompletedAt);
        Assert.Null(completed.Metadata.LastError);
    }

    [Fact]
    public void ShouldMarkAsCompleteWithError_WhenCompletingWithError()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var errorMessage = "Task failed due to network timeout";

        // Act
        var completed = context.Complete(errorMessage);

        // Assert
        Assert.True(completed.Metadata.IsComplete);
        Assert.NotNull(completed.Metadata.CompletedAt);
        Assert.Equal(errorMessage, completed.Metadata.LastError);
    }

    [Fact]
    public void ShouldSetCompletedAtToCurrentTime_WhenCompleting()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var before = DateTime.UtcNow;

        // Act
        var completed = context.Complete();

        // Assert
        var after = DateTime.UtcNow;
        Assert.True(completed.Metadata.CompletedAt >= before);
        Assert.True(completed.Metadata.CompletedAt <= after);
    }

    #endregion

    #region Record Behavior Tests

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var variables = TaskVariables.CreateBuilder().AddString("key", "value").Build();
        var outputs = ImmutableArray.Create(TaskOutput.Text("output"));
        var toolCalls = ImmutableArray.Create(ToolCall.Create("Tool", ToolArguments.Empty));
        var metadata = ExecutionMetadata.CreateNew();

        var context1 = new TypedTaskExecutionContext(taskId, agentId, variables, outputs, metadata, toolCalls);
        var context2 = new TypedTaskExecutionContext(taskId, agentId, variables, outputs, metadata, toolCalls);

        // Act & Assert
        Assert.Equal(context1, context2);
        Assert.True(context1 == context2);
        Assert.False(context1 != context2);
        Assert.Equal(context1.GetHashCode(), context2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var taskId1 = TaskId.From(Guid.NewGuid());
        var taskId2 = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var context1 = TypedTaskExecutionContext.Create(taskId1, agentId);
        var context2 = TypedTaskExecutionContext.Create(taskId2, agentId);

        // Act & Assert
        Assert.NotEqual(context1, context2);
        Assert.False(context1 == context2);
        Assert.True(context1 != context2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWith()
    {
        // Arrange
        var original = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));
        var newVariables = TaskVariables.CreateBuilder().AddString("new", "variable").Build();

        // Act
        var modified = original with { Variables = newVariables };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Same(TaskVariables.Empty, original.Variables);
        Assert.Same(newVariables, modified.Variables);
        Assert.Equal(original.TaskId, modified.TaskId);
        Assert.Equal(original.ExecutingAgent, modified.ExecutingAgent);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var context = TypedTaskExecutionContext.Create(TaskId.From(Guid.NewGuid()), AgentId.From(Guid.NewGuid()));

        // Act
        var result = context.ToString();

        // Assert
        Assert.Contains("TypedTaskExecutionContext", result);
        Assert.Contains("TaskId", result);
        Assert.Contains("ExecutingAgent", result);
    }

    [Fact]
    public void ShouldReturnAllComponents_WhenUsingDeconstruct()
    {
        // Arrange
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var variables = TaskVariables.CreateBuilder().AddInt("count", 10).Build();
        var outputs = ImmutableArray.Create(TaskOutput.Text("test"));
        var toolCalls = ImmutableArray.Create(ToolCall.Create("TestTool", ToolArguments.Empty));
        var metadata = ExecutionMetadata.CreateNew();

        var context = new TypedTaskExecutionContext(taskId, agentId, variables, outputs, metadata, toolCalls);

        // Act
        var (tId, agent, vars, prevOutputs, meta, tools) = context;

        // Assert
        Assert.Equal(taskId, tId);
        Assert.Equal(agentId, agent);
        Assert.Same(variables, vars);
        Assert.True(outputs.SequenceEqual(prevOutputs));
        Assert.Same(metadata, meta);
        Assert.True(toolCalls.SequenceEqual(tools));
    }

    #endregion

    #region ExecutionMetadata Tests

    [Fact]
    public void ShouldCreateWithDefaults_WhenUsingExecutionMetadataCreatingNew()
    {
        // Act
        var metadata = ExecutionMetadata.CreateNew();

        // Assert
        Assert.True(metadata.StartedAt <= DateTime.UtcNow);
        Assert.Null(metadata.CompletedAt);
        Assert.Null(metadata.MaxExecutionTime);
        Assert.NotNull(metadata.ExecutionId);
        Assert.Null(metadata.ParentExecutionId);
        Assert.Equal(0, metadata.RetryCount);
        Assert.Null(metadata.LastError);
        Assert.Empty(metadata.Tags);
        Assert.Empty(metadata.CustomProperties);
        Assert.False(metadata.IsComplete);
        Assert.Null(metadata.Duration);
        Assert.False(metadata.IsTimedOut);
    }

    [Fact]
    public void ShouldSetParameters_WhenUsingExecutionMetadataCreatingNewWithParameters()
    {
        // Arrange
        var maxTime = TimeoutStandard;
        var parentId = "parent-123";
        var tags = ImmutableDictionary<string, string>.Empty.Add("env", "test");

        // Act
        var metadata = ExecutionMetadata.CreateNew(maxTime, parentId, tags);

        // Assert
        Assert.Equal(maxTime, metadata.MaxExecutionTime);
        Assert.Equal(parentId, metadata.ParentExecutionId);
        Assert.Equal("test", metadata.Tags["env"]);
    }

    [Fact]
    public void ShouldMarkComplete_WhenUsingExecutionMetadataWithCompleteWithoutError()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();

        // Act
        var completed = metadata.Complete();

        // Assert
        Assert.False(metadata.IsComplete);
        Assert.True(completed.IsComplete);
        Assert.NotNull(completed.CompletedAt);
        Assert.Null(completed.LastError);
        Assert.NotNull(completed.Duration);
    }

    [Fact]
    public void ShouldSetError_WhenUsingExecutionMetadataWithCompleteWithError()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();
        var error = "Execution failed";

        // Act
        var completed = metadata.Complete(error);

        // Assert
        Assert.True(completed.IsComplete);
        Assert.Equal(error, completed.LastError);
    }

    [Fact]
    public void ShouldIncrementRetryCount_WhenUsingExecutionMetadataWithRetry()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();
        var error = "First retry";

        // Act
        var retried = metadata.WithRetry(error);

        // Assert
        Assert.Equal(0, metadata.RetryCount);
        Assert.Equal(1, retried.RetryCount);
        Assert.Equal(error, retried.LastError);
    }

    [Fact]
    public void ShouldAddTag_WhenUsingExecutionMetadataWithTag()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();

        // Act
        var tagged = metadata.WithTag("environment", "production");

        // Assert
        Assert.Empty(metadata.Tags);
        Assert.Equal("production", tagged.Tags["environment"]);
    }

    [Fact]
    public void ShouldAddProperty_WhenUsingExecutionMetadataWithCustomProperty()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();
        var customData = new { version = "1.0", timestamp = DateTime.UtcNow };

        // Act
        var updated = metadata.WithCustomProperty("customData", customData);

        // Assert
        Assert.Empty(metadata.CustomProperties);
        Assert.Same(customData, updated.CustomProperties["customData"]);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingExecutionMetadataUsingIsTimedOutWhenExceedsMaxTime()
    {
        // Arrange
        var maxTime = TimeSpan.FromSeconds(1);
        var metadata = ExecutionMetadata.CreateNew(maxTime);

        // Simulate elapsed time by completing strictly after max time (deterministic, R5.6)
        ClockAdvance.Until(() => DateTime.UtcNow - metadata.StartedAt > maxTime);
        var completed = metadata.Complete();

        // Act
        var isTimedOut = completed.IsTimedOut;

        // Assert
        Assert.True(isTimedOut);
        Assert.True(completed.Duration > maxTime);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingExecutionMetadataUsingIsTimedOutWhenWithinMaxTime()
    {
        // Arrange
        var maxTime = TimeoutExtended;
        var metadata = ExecutionMetadata.CreateNew(maxTime);

        // Act - Complete immediately
        var completed = metadata.Complete();

        // Assert
        Assert.False(completed.IsTimedOut);
        Assert.True(completed.Duration < maxTime);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingExecutionMetadataUsingDurationWhenNotComplete()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();

        // Act
        var duration = metadata.Duration;

        // Assert
        Assert.Null(duration);
        Assert.False(metadata.IsComplete);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFullTaskExecutionLifecycle_WhenUsingComplexScenario()
    {
        // Arrange - Create initial context
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var maxExecutionTime = TimeoutStandard;

        var context = TypedTaskExecutionContext.Create(taskId, agentId, maxExecutionTime);

        // Act - Simulate full execution lifecycle
        // 1. Add initial variables
        context = context
            .WithVariable("inputFile", "/data/input.csv")
            .WithVariable("outputFormat", "json")
            .WithVariable("batchSize", 1000);

        // 2. Add previous outputs from dependencies
        var dependencyOutput1 = TaskOutput.Text("Data validation completed: 10,000 records valid");
        var dependencyOutput2 = TaskOutput.Json("{\"status\": \"ready\", \"recordCount\": 10000}");
        context = context
            .WithPreviousOutput(dependencyOutput1)
            .WithPreviousOutput(dependencyOutput2);

        // 3. Record tool calls during execution
        var fileReadCall = ToolCall.Create("FileReader", ToolArguments.CreateBuilder()
            .AddString(ParamPath, "/data/input.csv")
            .AddString("encoding", "utf-8")
            .AddInt("bufferSize", 8192)
            .Build());

        var dataProcessorCall = ToolCall.Create("DataProcessor", ToolArguments.CreateBuilder()
            .AddString("operation", "transform")
            .AddInt("batchSize", 1000)
            .AddBool("validateOutput", true)
            .Build());

        var reportGeneratorCall = ToolCall.Create("ReportGenerator", ToolArguments.CreateBuilder()
            .AddString("format", "json")
            .AddString("outputPath", "/data/output.json")
            .AddBool("includeMetadata", true)
            .Build());

        context = context
            .WithToolCall(fileReadCall)
            .WithToolCall(dataProcessorCall)
            .WithToolCall(reportGeneratorCall);

        // 4. Complete execution successfully
        var finalContext = context.Complete();

        // Assert - Verify complete execution state
        Assert.Equal(taskId, finalContext.TaskId);
        Assert.Equal(agentId, finalContext.ExecutingAgent);

        // Verify variables
        Assert.Equal("/data/input.csv", finalContext.Variables.GetString("inputFile"));
        Assert.Equal("json", finalContext.Variables.GetString("outputFormat"));
        Assert.Equal(1000, finalContext.Variables.GetInt("batchSize"));

        // Verify previous outputs
        Assert.Equal(2, finalContext.PreviousOutputs.Length);
        Assert.Same(dependencyOutput1, finalContext.PreviousOutputs[0]);
        Assert.Same(dependencyOutput2, finalContext.PreviousOutputs[1]);

        // Verify tool calls
        Assert.Equal(3, finalContext.ToolCalls.Length);
        Assert.Equal("FileReader", finalContext.ToolCalls[0].ToolName);
        Assert.Equal("DataProcessor", finalContext.ToolCalls[1].ToolName);
        Assert.Equal("ReportGenerator", finalContext.ToolCalls[2].ToolName);

        // Verify execution metadata
        Assert.True(finalContext.Metadata.IsComplete);
        Assert.NotNull(finalContext.Metadata.CompletedAt);
        Assert.Null(finalContext.Metadata.LastError);
        Assert.NotNull(finalContext.Metadata.Duration);
        Assert.Equal(maxExecutionTime, finalContext.Metadata.MaxExecutionTime);
        Assert.Equal(0, finalContext.Metadata.RetryCount);
    }

    [Fact]
    public void ShouldTaskExecutionWithRetries_WhenUsingComplexScenario()
    {
        // Arrange - Create context for a task that will need retries
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var context = TypedTaskExecutionContext.Create(taskId, agentId, TimeoutExtended);

        // Act - Simulate execution with retries
        // 1. Initial setup
        context = context.WithVariable("apiEndpoint", "https://api.example.com/data");

        // 2. First attempt - tool call and failure
        var firstAttemptCall = ToolCall.Create("HttpClient", ToolArguments.CreateBuilder()
            .AddString(ParamUrl, "https://api.example.com/data")
            .AddInt("timeout", 30)
            .Build());
        context = context.WithToolCall(firstAttemptCall);

        // 3. First retry - update metadata and try again
        var retriedMetadata = context.Metadata.WithRetry("Network timeout error");
        context = context with { Metadata = retriedMetadata };

        var retryCall = ToolCall.Create("HttpClient", ToolArguments.CreateBuilder()
            .AddString(ParamUrl, "https://api.example.com/data")
            .AddInt("timeout", 60) // Increased timeout
            .AddBool("useRetryPolicy", true)
            .Build());
        context = context.WithToolCall(retryCall);

        // 4. Second retry with different approach
        var secondRetryMetadata = context.Metadata.WithRetry("Connection refused");
        context = context with { Metadata = secondRetryMetadata };

        var fallbackCall = ToolCall.Create("HttpClient", ToolArguments.CreateBuilder()
            .AddString(ParamUrl, "https://backup-api.example.com/data")
            .AddInt("timeout", 90)
            .AddString("fallbackMode", "enabled")
            .Build());
        context = context.WithToolCall(fallbackCall);

        // 5. Success on third attempt
        var successOutput = TaskOutput.Json("{\"data\": \"retrieved\", \"source\": \"backup\"}");
        context = context.WithPreviousOutput(successOutput);

        // 6. Complete successfully
        var finalContext = context.Complete();

        // Assert - Verify retry scenario
        Assert.Equal(taskId, finalContext.TaskId);
        Assert.True(finalContext.Metadata.IsComplete);
        Assert.Equal(2, finalContext.Metadata.RetryCount);
        Assert.Null(finalContext.Metadata.LastError); // LastError is cleared on successful completion
        Assert.Equal(3, finalContext.ToolCalls.Length);
        Assert.Single(finalContext.PreviousOutputs);

        // Verify tool call progression
        Assert.Equal(30, finalContext.ToolCalls[0].Arguments.GetRequired<int>("timeout"));
        Assert.Equal(60, finalContext.ToolCalls[1].Arguments.GetRequired<int>("timeout"));
        Assert.Equal("https://backup-api.example.com/data", finalContext.ToolCalls[2].Arguments.GetRequiredString(ParamUrl));

        // Verify final success
        Assert.Contains("retrieved", finalContext.PreviousOutputs[0].RawOutput);
    }

    [Fact]
    public void ShouldDataPipelineExecution_WhenUsingComplexScenario()
    {
        // Arrange - Multi-step data pipeline execution
        var taskId = TaskId.From(Guid.NewGuid());
        var agentId = AgentId.From(Guid.NewGuid());
        var context = TypedTaskExecutionContext.Create(taskId, agentId, TimeSpan.FromHours(2));

        // Act - Simulate complex data pipeline
        // 1. Initial configuration
        context = context
            .WithVariable("sourceDatabase", "production_db")
            .WithVariable("targetDatabase", "analytics_db")
            .WithVariable("batchSize", 10000)
            .WithVariable("transformationRules", TransformationRules);

        // 2. Add metadata tags for tracking
        var taggedMetadata = context.Metadata
            .WithTag("pipeline", "customer-data-sync")
            .WithTag("environment", "production")
            .WithTag("team", "data-engineering")
            .WithCustomProperty("scheduledBy", "cron-job-daily-0300")
            .WithCustomProperty("expectedRecords", 1500000);
        context = context with { Metadata = taggedMetadata };

        // 3. Execute pipeline steps
        // Step 1: Data extraction
        var extractionCall = ToolCall.Create("DatabaseExtractor", ToolArguments.CreateBuilder()
            .AddString("source", "production_db")
            .AddString(ParamQuery, "SELECT * FROM customers WHERE updated_at >= @since")
            .AddString("since", "2024-01-01")
            .AddInt("batchSize", 10000)
            .Build());

        var extractionOutput = TaskOutput.Json("{\"extracted\": 1500000, \"batches\": 150, \"duration\": \"45m\"}");
        context = context.WithToolCall(extractionCall).WithPreviousOutput(extractionOutput);

        // Step 2: Data transformation
        var transformationCall = ToolCall.Create("DataTransformer", ToolArguments.CreateBuilder()
            .AddObject("rules", context.Variables.GetObject<string[]>("transformationRules")!)
            .AddInt("parallelThreads", 8)
            .AddBool("validateOutput", true)
            .AddDouble("errorThreshold", 0.01)
            .Build());

        var transformationOutput = TaskOutput.Json("{\"transformed\": 1498500, \"errors\": 1500, \"errorRate\": 0.001}");
        context = context.WithToolCall(transformationCall).WithPreviousOutput(transformationOutput);

        // Step 3: Data validation
        var validationCall = ToolCall.Create("DataValidator", ToolArguments.CreateBuilder()
            .AddString("schemaPath", "/schemas/customer-v2.json")
            .AddBool("strictMode", true)
            .AddInt("sampleSize", 10000)
            .Build());

        var validationOutput = TaskOutput.Json("{\"valid\": 1498500, \"invalid\": 0, \"validationRate\": 1.0}");
        context = context.WithToolCall(validationCall).WithPreviousOutput(validationOutput);

        // Step 4: Data loading
        var loadingCall = ToolCall.Create("DatabaseLoader", ToolArguments.CreateBuilder()
            .AddString("target", "analytics_db")
            .AddString("table", "customers_transformed")
            .AddString("mode", "upsert")
            .AddBool("createIndexes", true)
            .Build());

        var loadingOutput = TaskOutput.Json("{\"loaded\": 1498500, \"upserted\": 45000, \"inserted\": 1453500}");
        context = context.WithToolCall(loadingCall).WithPreviousOutput(loadingOutput);

        // 4. Complete pipeline
        var finalContext = context.Complete();

        // Assert - Verify complex pipeline execution
        Assert.Equal(taskId, finalContext.TaskId);
        Assert.True(finalContext.Metadata.IsComplete);
        Assert.Equal(0, finalContext.Metadata.RetryCount);
        Assert.Null(finalContext.Metadata.LastError);

        // Verify configuration
        Assert.Equal("production_db", finalContext.Variables.GetString("sourceDatabase"));
        Assert.Equal("analytics_db", finalContext.Variables.GetString("targetDatabase"));
        Assert.Equal(10000, finalContext.Variables.GetInt("batchSize"));

        var rules = finalContext.Variables.GetObject<string[]>("transformationRules");
        Assert.Equal(4, rules!.Length);
        Assert.Contains("normalize_phone_numbers", rules);

        // Verify metadata tags and properties
        Assert.Equal("customer-data-sync", finalContext.Metadata.Tags["pipeline"]);
        Assert.Equal("production", finalContext.Metadata.Tags["environment"]);
        Assert.Equal("cron-job-daily-0300", finalContext.Metadata.CustomProperties["scheduledBy"]);
        Assert.Equal(1500000, finalContext.Metadata.CustomProperties["expectedRecords"]);

        // Verify pipeline steps
        Assert.Equal(4, finalContext.ToolCalls.Length);
        Assert.Equal("DatabaseExtractor", finalContext.ToolCalls[0].ToolName);
        Assert.Equal("DataTransformer", finalContext.ToolCalls[1].ToolName);
        Assert.Equal("DataValidator", finalContext.ToolCalls[2].ToolName);
        Assert.Equal("DatabaseLoader", finalContext.ToolCalls[3].ToolName);

        // Verify step outputs
        Assert.Equal(4, finalContext.PreviousOutputs.Length);
        Assert.Contains("1500000", finalContext.PreviousOutputs[0].RawOutput); // Extraction
        Assert.Contains("1498500", finalContext.PreviousOutputs[1].RawOutput); // Transformation
        Assert.Contains("validationRate", finalContext.PreviousOutputs[2].RawOutput); // Validation
        Assert.Contains("loaded", finalContext.PreviousOutputs[3].RawOutput); // Loading

        // Verify execution time tracking
        Assert.NotNull(finalContext.Metadata.Duration);
        Assert.Equal(TimeSpan.FromHours(2), finalContext.Metadata.MaxExecutionTime);
        Assert.False(finalContext.Metadata.IsTimedOut);
    }

    #endregion
}
