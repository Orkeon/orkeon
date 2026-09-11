using System.Diagnostics;
using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.Telemetry;

/// <summary>
/// Static helper class providing convenience methods for creating and managing
/// OpenTelemetry tracing spans for Orkeon operations.
/// </summary>
public static class TracingInstrumentation
{
    /// <summary>
    /// Starts a crew execution span.
    /// </summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="crewName">The crew name.</param>
    /// <param name="process">The process type (sequential, hierarchical, parallel).</param>
    /// <returns>An Activity if a listener is registered, null otherwise.</returns>
    public static Activity? StartCrewExecution(string crewId, string? crewName = null, string? process = null)
    {
        var activity = OrkeonDiagnostics.CrewSource.StartActivity(
            "crew.execute",
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(OrkeonDiagnosticTags.CrewId, crewId);
            if (crewName is not null)
                activity.SetTag(OrkeonDiagnosticTags.CrewName, crewName);
            if (process is not null)
                activity.SetTag(OrkeonDiagnosticTags.CrewProcess, process);
        }

        return activity;
    }

    /// <summary>
    /// Starts a task execution span.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="taskName">The task name or description.</param>
    /// <param name="agentRole">The role of the agent executing the task.</param>
    /// <returns>An Activity if a listener is registered, null otherwise.</returns>
    public static Activity? StartTaskExecution(string taskId, string? taskName = null, string? agentRole = null)
    {
        var activity = OrkeonDiagnostics.TaskSource.StartActivity(
            "task.execute",
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(OrkeonDiagnosticTags.TaskId, taskId);
            if (taskName is not null)
                activity.SetTag(OrkeonDiagnosticTags.TaskName, taskName);
            if (agentRole is not null)
                activity.SetTag(OrkeonDiagnosticTags.AgentRole, agentRole);
        }

        return activity;
    }

    /// <summary>
    /// Starts an LLM call span with Client kind (outgoing call).
    /// </summary>
    /// <param name="provider">The LLM provider name (e.g., "OpenAI", "Ollama").</param>
    /// <param name="model">The model being called (e.g., "gpt-4").</param>
    /// <param name="agentRole">The role of the agent making the call.</param>
    /// <returns>An Activity if a listener is registered, null otherwise.</returns>
    public static Activity? StartLlmCall(string provider, string? model = null, string? agentRole = null)
    {
        var activity = OrkeonDiagnostics.LlmSource.StartActivity(
            GenAiAttributes.SpanName(GenAiAttributes.OperationChat, model),
            ActivityKind.Client);

        if (activity is not null)
        {
            activity.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationChat);
            activity.SetTag(OrkeonDiagnosticTags.LlmProvider, Application.Telemetry.OrkeonActivitySources.ProviderName(provider));
            if (model is not null)
                activity.SetTag(OrkeonDiagnosticTags.LlmModel, model);
            if (agentRole is not null)
                activity.SetTag(OrkeonDiagnosticTags.AgentRole, agentRole);
        }

        return activity;
    }

    /// <summary>
    /// Completes an LLM call span with token usage and cost information.
    /// </summary>
    /// <param name="activity">The activity to complete (can be null).</param>
    /// <param name="promptTokens">Number of prompt tokens used.</param>
    /// <param name="completionTokens">Number of completion tokens used.</param>
    /// <param name="estimatedCost">Estimated cost in USD.</param>
    /// <param name="success">Whether the call succeeded.</param>
    public static void CompleteLlmCall(
        Activity? activity,
        int promptTokens = 0,
        int completionTokens = 0,
        double estimatedCost = 0.0,
        bool success = true)
    {
        if (activity is null) return;

        activity.SetTag(OrkeonDiagnosticTags.LlmPromptTokens, promptTokens);
        activity.SetTag(OrkeonDiagnosticTags.LlmCompletionTokens, completionTokens);
        activity.SetTag(OrkeonDiagnosticTags.LlmTotalTokens, promptTokens + completionTokens);
        activity.SetTag(OrkeonDiagnosticTags.LlmCost, estimatedCost);
        activity.SetTag(OrkeonDiagnosticTags.LlmSuccess, success);

        if (!success)
        {
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    /// <summary>
    /// Starts a tool execution span.
    /// </summary>
    /// <param name="toolName">The name of the tool being executed.</param>
    /// <param name="agentRole">The role of the agent executing the tool.</param>
    /// <returns>An Activity if a listener is registered, null otherwise.</returns>
    public static Activity? StartToolExecution(string toolName, string? agentRole = null)
    {
        var activity = OrkeonDiagnostics.ToolSource.StartActivity(
            GenAiAttributes.SpanName(GenAiAttributes.OperationExecuteTool, toolName),
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(GenAiAttributes.OperationName, GenAiAttributes.OperationExecuteTool);
            activity.SetTag(OrkeonDiagnosticTags.ToolName, toolName);
            if (agentRole is not null)
                activity.SetTag(OrkeonDiagnosticTags.AgentRole, agentRole);
        }

        return activity;
    }

    /// <summary>
    /// Starts a memory operation span.
    /// </summary>
    /// <param name="operation">The type of memory operation (store, search, get, delete).</param>
    /// <param name="provider">The memory provider name.</param>
    /// <returns>An Activity if a listener is registered, null otherwise.</returns>
    public static Activity? StartMemoryOperation(string operation, string? provider = null)
    {
        var activity = OrkeonDiagnostics.MemorySource.StartActivity(
            $"memory.{operation}",
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(OrkeonDiagnosticTags.MemoryOperation, operation);
            if (provider is not null)
                activity.SetTag(OrkeonDiagnosticTags.MemoryProvider, provider);
        }

        return activity;
    }

    /// <summary>
    /// Records an exception on an activity, setting the error status and adding an exception event.
    /// </summary>
    /// <param name="activity">The activity to record the exception on (can be null).</param>
    /// <param name="exception">The exception to record.</param>
    public static void RecordException(Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (activity is null) return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(OrkeonDiagnosticTags.ErrorType, exception.GetType().Name);
        activity.SetTag(OrkeonDiagnosticTags.ErrorMessage, exception.Message);

        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace ?? string.Empty }
        };

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }
}
