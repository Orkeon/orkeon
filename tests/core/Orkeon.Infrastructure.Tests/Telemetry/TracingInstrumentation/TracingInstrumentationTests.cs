using System.Diagnostics;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Infrastructure.Tests.Telemetry;

public sealed class TracingInstrumentationTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public TracingInstrumentationTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Orkeon"),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _listener.Dispose();
    }

    [Fact]
    public void ShouldCreateActivityWithCorrectTags_WhenStartingCrewExecution()
    {
        // Act
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartCrewExecution(CrewId1, "TestCrew", "sequential");

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("crew.execute", activity!.DisplayName);
        Assert.Equal(CrewId1, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewId));
        Assert.Equal("TestCrew", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewName));
        Assert.Equal("sequential", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewProcess));
    }

    [Fact]
    public void ShouldOnlySetCrewId_WhenStartingCrewExecutionWithMinimalArgs()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartCrewExecution(CrewId2);

        Assert.NotNull(activity);
        Assert.Equal(CrewId2, activity!.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewId));
        Assert.Null(activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewName));
        Assert.Null(activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.CrewProcess));
    }

    [Fact]
    public void ShouldCreateActivityWithCorrectTags_WhenStartingTaskExecution()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartTaskExecution(TaskId1, "Research", RoleAnalyst);

        Assert.NotNull(activity);
        Assert.Equal("task.execute", activity!.DisplayName);
        Assert.Equal(TaskId1, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.TaskId));
        Assert.Equal("Research", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.TaskName));
        Assert.Equal(RoleAnalyst, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.AgentRole));
    }

    [Fact]
    public void ShouldCreateActivityWithClientKind_WhenStartingLlmCall()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartLlmCall("OpenAI", ModelGpt4, "Researcher");

        Assert.NotNull(activity);
        Assert.Equal(ActivityKind.Client, activity!.Kind);
        Assert.Equal($"chat {ModelGpt4}", activity.DisplayName);
        Assert.Equal("chat", activity.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("openai", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmProvider));
        Assert.Equal(ModelGpt4, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmModel));
        Assert.Equal("Researcher", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.AgentRole));
    }

    [Fact]
    public void ShouldSetTokenTags_WhenCompletingLlmCall()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartLlmCall("OpenAI", ModelGpt4);

        // Act
        global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.CompleteLlmCall(activity, promptTokens: 100, completionTokens: 50, estimatedCost: 0.015, success: true);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal(100, activity!.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmPromptTokens));
        Assert.Equal(50, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmCompletionTokens));
        Assert.Equal(150, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmTotalTokens));
        Assert.Equal(0.015, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmCost));
        Assert.True((bool)activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmSuccess)!);
    }

    [Fact]
    public void ShouldSetErrorStatus_WhenLlmCallFails()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartLlmCall("OpenAI", ModelGpt4);

        global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.CompleteLlmCall(activity, success: false);

        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Error, activity!.Status);
        Assert.False((bool)activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmSuccess)!);
    }

    [Fact]
    public void ShouldNotThrow_WhenCompletingLlmCallWithNullActivity()
    {
        // Should not throw
        var exception = Record.Exception(() => global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.CompleteLlmCall(null, promptTokens: 100));
        Assert.Null(exception);
    }

    [Fact]
    public void ShouldCreateActivityWithCorrectTags_WhenStartingToolExecution()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartToolExecution(ToolFileRead, "Researcher");

        Assert.NotNull(activity);
        Assert.Equal($"execute_tool {ToolFileRead}", activity!.DisplayName);
        Assert.Equal("execute_tool", activity.GetTagItem("gen_ai.operation.name"));
        Assert.Equal(ToolFileRead, activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.ToolName));
        Assert.Equal("Researcher", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.AgentRole));
    }

    [Fact]
    public void ShouldCreateActivityWithCorrectTags_WhenStartingMemoryOperation()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartMemoryOperation("search", "Redis");

        Assert.NotNull(activity);
        Assert.Equal("memory.search", activity!.DisplayName);
        Assert.Equal("search", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.MemoryOperation));
        Assert.Equal("Redis", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.MemoryProvider));
    }

    [Fact]
    public void ShouldSetErrorStatusAndAddEvent_WhenRecordingException()
    {
        using var activity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartLlmCall("OpenAI");
        var exception = new InvalidOperationException("Test error");

        // Act
        global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.RecordException(activity, exception);

        // Assert
        Assert.NotNull(activity);
        Assert.Equal(ActivityStatusCode.Error, activity!.Status);
        Assert.Equal("Test error", activity.StatusDescription);
        Assert.Equal("InvalidOperationException", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.ErrorType));
        Assert.Equal("Test error", activity.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.ErrorMessage));
        Assert.Contains(activity.Events, e => e.Name == "exception");
    }

    [Fact]
    public void ShouldNotThrow_WhenRecordingExceptionWithNullActivity()
    {
        var exception = Record.Exception(() => global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.RecordException(null, new Exception("test")));
        Assert.Null(exception);
    }

    [Fact]
    public void ShouldMaintainParentChildRelationship_WhenNestingSpans()
    {
        using var crewActivity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartCrewExecution(CrewId1);
        using var taskActivity = global::Orkeon.Infrastructure.Telemetry.TracingInstrumentation.StartTaskExecution(TaskId1);

        // The task activity should have the crew activity as its parent
        Assert.NotNull(crewActivity);
        Assert.NotNull(taskActivity);
        Assert.Equal(crewActivity!.Id, taskActivity!.ParentId);
    }

    [Fact]
    public void ShouldReturnNullActivity_WhenNoListenerIsRegistered()
    {
        // Dispose the listener so no activities are created
        _listener.Dispose();

        // Use a name that does NOT match any active listener pattern ("Orkeon*")
        using var source = new ActivitySource("Unlistened.Test.Source", "1.0.0");
        var activity = source.StartActivity("test");

        Assert.Null(activity);
    }
}
