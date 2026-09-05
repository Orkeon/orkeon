namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Where a <see cref="JsLlmFacade"/> reports what it does: streamed deltas, its own
/// diagnostics, and per-call usage - together with the crew/agent identity those usage
/// events are attributed to. Bundled to keep the facade constructor readable; every
/// member stays optional, and an absent bundle means nothing is observed, which is the
/// behaviour of a host that wired no sinks.
/// </summary>
internal sealed record JsLlmObservability
{
    /// <summary>
    /// Host-native renderer for streamed <c>ctx.llm.act</c> deltas. Null = buffered
    /// behaviour unless the script passes <c>onDelta</c>.
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? DeltaSink { get; init; }

    /// <summary>Diagnostic sink for the facade's own progress logs. Null = silent.</summary>
    public Microsoft.Extensions.Logging.ILogger? Logger { get; init; }

    /// <summary>
    /// Receiver for per-call LLM usage events. Null = usage not observed (behaviour
    /// byte-identical to before the sink existed).
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.ILlmUsageSink? UsageSink { get; init; }

    /// <summary>Crew id stamped on usage events; null is recorded as the empty string.</summary>
    public string? CrewName { get; init; }

    /// <summary>Agent id stamped on usage events; null is recorded as the empty string.</summary>
    public string? AgentName { get; init; }
}
