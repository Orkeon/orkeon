namespace Orkeon.Scripting;

/// <summary>
/// The host-side ports a scripting engine hands to its bindings: the gate consulted
/// before a scripted tool call runs, plus the sink the <c>ctx.llm</c> surface streams
/// deltas to. Bundled so <see cref="JsEngineFactory"/> keeps a readable constructor; every
/// member stays individually optional and a null bundle simply means the host wired none
/// of them. Usage is not a port here: the provider the host hands the engine is metered
/// where the host builds it (STUDIO-42).
/// </summary>
public sealed record ScriptingHostPorts
{
    /// <summary>
    /// Per-tool-call permission gate consulted by <c>ctx.llm.act</c> before each tool
    /// execution. Null = ungated.
    /// </summary>
    public Orkeon.Application.Interfaces.Security.IPermissionGate? PermissionGate { get; init; }

    /// <summary>
    /// Host-native renderer for streamed <c>ctx.llm.act</c> deltas. Null = buffered
    /// behaviour unless the script passes <c>onDelta</c>.
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? DeltaSink { get; init; }

    /// <summary>
    /// The single tool-invocation point (GAP-09) every <c>ctx.llm.act</c> tool call goes
    /// through once the permission gate allowed it: guardian, truncation, result sanitizer,
    /// audit. Null = the tool is called directly.
    /// </summary>
    public Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? ToolInvocation { get; init; }

    /// <summary>
    /// The host's named LLM profiles behind <c>llm.profile(name)</c> (GAP-17). Null offers the
    /// default profile alone: <c>llm.profile</c> of any other name throws, listing it.
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? LlmProfiles { get; init; }

    /// <summary>
    /// The durable store behind <c>ctx.memory.agent</c> (RT-16). Null keeps the scope in memory.
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.IScriptAgentMemoryStore? AgentMemoryStore { get; init; }

    /// <summary>The cut of each tool result and the fan-out bound of <c>ctx.llm.act</c>. Null keeps the defaults.</summary>
    public Orkeon.Domain.Tools.ToolResultLimitsOptions? ToolLimits { get; init; }

    /// <summary>The transcript caps of an agent instance. Null keeps the defaults.</summary>
    public Orkeon.Scripting.Configuration.ScriptAgentInstanceOptions? AgentInstances { get; init; }
}
