namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Where a <see cref="JsLlmFacade"/> reports what it does: streamed deltas and its own
/// diagnostics — together with the crew/agent identity its LLM calls are attributed to on
/// the token meter. Bundled to keep the facade constructor readable; every member stays
/// optional, and an absent bundle means nothing is observed, which is the behaviour of a
/// host that wired no sinks.
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

    /// <summary>The crew the facade's calls are attributed to; null names none.</summary>
    public string? CrewName { get; init; }

    /// <summary>The agent the facade's calls are attributed to; null names none.</summary>
    public string? AgentName { get; init; }
}
