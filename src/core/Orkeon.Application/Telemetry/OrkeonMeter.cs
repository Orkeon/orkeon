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

    private static readonly Meter s_meter = new(Name, OrkeonActivitySources.Version);

    private static readonly Histogram<double> s_duration = s_meter.CreateHistogram<double>(
        "gen_ai.client.operation.duration", "s", "Duration of a model call");

    private static readonly Histogram<long> s_tokens = s_meter.CreateHistogram<long>(
        "gen_ai.client.token.usage", "{token}", "Tokens a model call reported");

    private static readonly Counter<long> s_calls = s_meter.CreateCounter<long>(
        "orkeon.llm.calls", "{call}", "Model calls");

    private static readonly Lock s_costGate = new();
    private static double s_costTotalUsd;

    static OrkeonMeter()
    {
        // Declared, not yet fed: a listener reads zero for them until the crew, task, tool and
        // security paths record. The meter owns them: no reference to hold.
        s_meter.CreateCounter<long>("orkeon.tool.executions", "{execution}", "Total number of tool executions");
        s_meter.CreateCounter<long>("orkeon.task.executions", "{execution}", "Total number of task executions");
        s_meter.CreateCounter<long>("orkeon.crew.executions", "{execution}", "Total number of crew executions");
        s_meter.CreateCounter<long>("orkeon.security.events", "{event}", "Total number of security events");
        s_meter.CreateHistogram<double>("orkeon.tool.duration", "ms", "Duration of tool executions in milliseconds");
        s_meter.CreateHistogram<double>("orkeon.task.duration", "ms", "Duration of task executions in milliseconds");
        s_meter.CreateHistogram<double>("orkeon.crew.duration", "ms", "Duration of crew executions in milliseconds");
        s_meter.CreateUpDownCounter<long>("orkeon.crew.active", "{crew}", "Number of currently active crew executions");
        s_meter.CreateUpDownCounter<long>("orkeon.task.active", "{task}", "Number of currently active task executions");
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

    /// <summary>Adds a cost the vendor billed in USD to the process-wide total.</summary>
    /// <param name="usd">The amount in USD.</param>
    public static void AddCostUsd(double usd)
    {
        lock (s_costGate)
            s_costTotalUsd += usd;
    }
}
