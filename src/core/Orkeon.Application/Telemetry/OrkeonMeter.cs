using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Orkeon.Application.Telemetry;

/// <summary>
/// The static holder of the Orkeon meter: every model call is measured once on it, whether a usage
/// sink listens or not. Attributes are bounded (operation, provider, model, error type, token type);
/// no identifier of a crew, task or agent ever becomes a tag.
/// </summary>
public static class OrkeonMeter
{
    /// <summary>The meter name.</summary>
    public const string Name = "Orkeon";

    /// <summary>The operation attribute.</summary>
    public const string OperationAttribute = "gen_ai.operation.name";

    /// <summary>The provider attribute.</summary>
    public const string ProviderAttribute = "gen_ai.provider.name";

    /// <summary>The requested model attribute.</summary>
    public const string ModelAttribute = "gen_ai.request.model";

    /// <summary>The exception type attribute of a failed call.</summary>
    public const string ErrorTypeAttribute = "error.type";

    /// <summary>The token direction attribute, <c>input</c> or <c>output</c>.</summary>
    public const string TokenTypeAttribute = "gen_ai.token.type";

    /// <summary>The tool name attribute.</summary>
    public const string ToolNameAttribute = "orkeon.tool.name";

    /// <summary>The tool call outcome attribute: success, failure, blocked or cancelled.</summary>
    public const string OutcomeAttribute = "orkeon.outcome";

    /// <summary>The security event kind attribute: block, warn or injection.</summary>
    public const string SecurityKindAttribute = "orkeon.security.kind";

    private static readonly Meter s_meter = new(Name, OrkeonActivitySources.Version);

    private static readonly Histogram<double> s_duration = s_meter.CreateHistogram<double>(
        "gen_ai.client.operation.duration", "s", "Duration of a model call");

    private static readonly Histogram<long> s_tokens = s_meter.CreateHistogram<long>(
        "gen_ai.client.token.usage", "{token}", "Tokens a model call reported");

    private static readonly Counter<long> s_calls = s_meter.CreateCounter<long>(
        "orkeon.llm.calls", "{call}", "Model calls");

    private static readonly Counter<long> s_toolExecutions = s_meter.CreateCounter<long>(
        "orkeon.tool.executions", "{execution}", "Total number of tool executions");

    private static readonly Histogram<double> s_toolDuration = s_meter.CreateHistogram<double>(
        "orkeon.tool.duration", "s", "Duration of tool executions in seconds");

    private static readonly Counter<long> s_securityEvents = s_meter.CreateCounter<long>(
        "orkeon.security.events", "{event}", "Total number of security events");

    private static readonly Counter<long> s_taskExecutions = s_meter.CreateCounter<long>(
        "orkeon.task.executions", "{execution}", "Total number of task executions");

    private static readonly Counter<long> s_crewExecutions = s_meter.CreateCounter<long>(
        "orkeon.crew.executions", "{execution}", "Total number of crew executions");

    private static readonly Histogram<double> s_taskDuration = s_meter.CreateHistogram<double>(
        "orkeon.task.duration", "s", "Duration of task executions in seconds");

    private static readonly Histogram<double> s_crewDuration = s_meter.CreateHistogram<double>(
        "orkeon.crew.duration", "s", "Duration of crew executions in seconds");

    private static readonly UpDownCounter<long> s_crewActive = s_meter.CreateUpDownCounter<long>(
        "orkeon.crew.active", "{crew}", "Number of currently active crew executions");

    private static readonly UpDownCounter<long> s_taskActive = s_meter.CreateUpDownCounter<long>(
        "orkeon.task.active", "{task}", "Number of currently active task executions");

    private static readonly Lock s_costGate = new();
    private static double s_costTotalUsd;

    static OrkeonMeter()
    {
        // Declared, not yet fed: a listener reads zero for them until the call path records. The meter
        // owns them: no reference to hold.
        s_meter.CreateUpDownCounter<long>("orkeon.llm.active", "{call}", "Number of currently active LLM calls");
        // The meter owns the gauge: no reference to hold.
        s_meter.CreateObservableGauge(
            "orkeon.cost.total_usd", ReadCostTotal, "USD", "Cost the vendors billed in USD since the process started");
    }

    /// <summary>
    /// The meter. Reading it creates every instrument, so a listener sees them (the cost gauge
    /// included) as soon as a metered provider exists, before its first call.
    /// </summary>
    public static Meter Meter => s_meter;

    private static double ReadCostTotal()
    {
        lock (s_costGate)
            return s_costTotalUsd;
    }

    /// <summary>Measures one model call.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model, or empty when none is known.</param>
    /// <param name="seconds">The call duration in seconds.</param>
    /// <param name="errorType">The full name of the exception type of a failed call; null on success.</param>
    /// <param name="inputTokens">Reported input tokens; null when the vendor reported none.</param>
    /// <param name="outputTokens">Reported output tokens; null when the vendor reported none.</param>
    public static void RecordLlmCall(
        string provider, string model, double seconds, string? errorType, long? inputTokens, long? outputTokens)
    {
        var tags = new TagList
        {
            { OperationAttribute, "chat" },
            { ProviderAttribute, provider },
            { ModelAttribute, model },
        };
        s_calls.Add(1, tags);

        var durationTags = tags;
        if (errorType is not null)
            durationTags.Add(ErrorTypeAttribute, errorType);
        s_duration.Record(seconds, durationTags);

        if (inputTokens is { } input)
        {
            var inputTags = tags;
            inputTags.Add(TokenTypeAttribute, "input");
            s_tokens.Record(input, inputTags);
        }

        if (outputTokens is { } output)
        {
            var outputTags = tags;
            outputTags.Add(TokenTypeAttribute, "output");
            s_tokens.Record(output, outputTags);
        }
    }

    /// <summary>Measures the end of one tool call.</summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="outcome">success, failure, blocked or cancelled.</param>
    /// <param name="seconds">The call duration in seconds; null when the call never ran (blocked).</param>
    public static void RecordToolExecution(string toolName, string outcome, double? seconds)
    {
        var tags = new TagList { { ToolNameAttribute, toolName }, { OutcomeAttribute, outcome } };
        s_toolExecutions.Add(1, tags);
        if (seconds is { } value)
            s_toolDuration.Record(value, tags);
    }

    internal static void CrewStarted(string crew, string process) =>
        s_crewActive.Add(1, CrewTags(crew, process));

    internal static void CrewEnded(string crew, string process, string outcome, double seconds)
    {
        s_crewActive.Add(-1, CrewTags(crew, process));
        var tags = CrewTags(crew, process);
        tags.Add(OutcomeAttribute, outcome);
        s_crewExecutions.Add(1, tags);
        s_crewDuration.Record(seconds, tags);
    }

    internal static void TaskStarted(string crew, string? role) =>
        s_taskActive.Add(1, TaskTags(crew, role));

    internal static void TaskEnded(string crew, string? role, string outcome, double seconds)
    {
        s_taskActive.Add(-1, TaskTags(crew, role));
        var tags = TaskTags(crew, role);
        tags.Add(OutcomeAttribute, outcome);
        s_taskExecutions.Add(1, tags);
        s_taskDuration.Record(seconds, tags);
    }

    private static TagList CrewTags(string crew, string process) =>
        new() { { "orkeon.crew.name", crew }, { "orkeon.crew.process", process } };

    private static TagList TaskTags(string crew, string? role)
    {
        var tags = new TagList { { "orkeon.crew.name", crew } };
        if (!string.IsNullOrEmpty(role))
            tags.Add("orkeon.agent.role", role);
        return tags;
    }

    /// <summary>Counts one security event.</summary>
    /// <param name="kind">block, warn or injection.</param>
    /// <param name="toolName">The tool involved; null or empty when none is known.</param>
    public static void RecordSecurityEvent(string kind, string? toolName)
    {
        var tags = new TagList { { SecurityKindAttribute, kind } };
        if (!string.IsNullOrEmpty(toolName))
            tags.Add(ToolNameAttribute, toolName);
        s_securityEvents.Add(1, tags);
    }

    /// <summary>Adds a cost the vendor billed in USD to the process-wide total.</summary>
    /// <param name="usd">The amount in USD.</param>
    public static void AddCostUsd(double usd)
    {
        lock (s_costGate)
            s_costTotalUsd += usd;
    }
}
