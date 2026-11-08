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

    /// <summary>
    /// The host's DLP for the tool calls the model streams (FWK-20), independent of the delta sink: the arguments
    /// of each call reach <c>onToolCallDelta</c> and the sink held until the call ends, scanned whole. Null streams
    /// the fragments as they arrive.
    /// </summary>
    public Orkeon.Application.Crew.ToolCallDeltaDlp? ToolCallDlp { get; init; }

    /// <summary>Diagnostic sink for the facade's own progress logs. Null = silent.</summary>
    public Microsoft.Extensions.Logging.ILogger? Logger { get; init; }

    /// <summary>The crew the facade's calls are attributed to; null names none.</summary>
    public string? CrewName { get; init; }

    /// <summary>The agent the facade's calls are attributed to; null names none.</summary>
    public string? AgentName { get; init; }

    /// <summary>
    /// The single tool-invocation point (GAP-09) an allowed <c>ctx.llm.act</c> tool call goes
    /// through: guardian, truncation, result sanitizer, audit. Null = the tool is called directly.
    /// </summary>
    public Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? ToolInvocation { get; init; }
}
