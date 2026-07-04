using System.Diagnostics;
using Orkeon.Infrastructure.Telemetry;

namespace Orkeon.Infrastructure.Tests.Telemetry;

public sealed class TracingInstrumentationTestsFixture : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public TracingInstrumentationTestsFixture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Orkeon"),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public static Activity? StartCrewExecution(string crewId, string? crewName = null, string? processType = null)
        => TracingInstrumentation.StartCrewExecution(crewId, crewName, processType);

    public static Activity? StartTaskExecution(string taskId, string? taskName = null, string? agentRole = null)
        => TracingInstrumentation.StartTaskExecution(taskId, taskName, agentRole);

    public static Activity? StartLlmCall(string provider, string? model = null, string? agentRole = null)
        => TracingInstrumentation.StartLlmCall(provider, model, agentRole);

    public static void CompleteLlmCall(Activity? activity, int promptTokens = 0, int completionTokens = 0, double estimatedCost = 0, bool success = true)
        => TracingInstrumentation.CompleteLlmCall(activity, promptTokens: promptTokens, completionTokens: completionTokens, estimatedCost: estimatedCost, success: success);

    public static Activity? StartToolExecution(string toolName, string? agentRole = null)
        => TracingInstrumentation.StartToolExecution(toolName, agentRole);

    public static Activity? StartMemoryOperation(string operation, string? provider = null)
        => TracingInstrumentation.StartMemoryOperation(operation, provider);

    public static void RecordException(Activity? activity, Exception exception)
        => TracingInstrumentation.RecordException(activity, exception);

    public List<Activity> GetActivities() => _activities;

    public void DisposeListener() => _listener.Dispose();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _listener.Dispose();
    }
}
