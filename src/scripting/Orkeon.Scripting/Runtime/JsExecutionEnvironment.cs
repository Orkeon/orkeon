using Jint;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel;
using Orkeon.Scripting.Builders;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Bundles the ambient runtime dependencies shared by every <see cref="JsExecutionContext"/>
/// (and its <see cref="JsAgentContext"/> subclass). Introduced to keep the context
/// constructors below the parameter-count threshold while grouping cohesive collaborators
/// (engine, current agent, crew, channel, memory, logger, cancellation, optional LLM).
/// </summary>
internal sealed record JsExecutionEnvironment
{
    public required Engine Engine { get; init; }
    public required JsAgent Self { get; init; }
    public required JsCrew Crew { get; init; }
    public required JsAgentChannel Channel { get; init; }
    public required JsMemoryScope CrewMemory { get; init; }
    public required ILogger Logger { get; init; }
    public required CancellationToken Ct { get; init; }
    public ILlmProvider? LlmProvider { get; init; }

    /// <summary>Built-in tool instances available to <c>ctx.llm.act</c> (filtered per-agent in the context).</summary>
    public IReadOnlyList<Orkeon.Domain.Tools.IBaseTool>? BuiltInTools { get; init; }

    /// <summary>
    /// Typed execution budget for the current crew run (bridged from
    /// <c>crewBuilder().budget({...})</c>), shared by every agent of the run. Null when the
    /// crew declared no budget — <c>ctx.llm.act</c> then enforces nothing (pre-F1 behaviour).
    /// </summary>
    public Orkeon.Domain.Autonomous.AgentExecutionBudget? Budget { get; init; }

    /// <summary>The permit of the crew's concurrency gate the agent runs under as a child, null for a run holding none.</summary>
    internal GatePermit? Permit { get; init; }

    /// <summary>The agent instance (RT-03) this agent runs as, null for a run that is not one.</summary>
    internal AgentInstanceRun? Instance { get; init; }

    /// <summary>
    /// Optional per-tool-call permission gate consulted by <c>ctx.llm.act</c> before each
    /// tool execution. Null = ungated (pre-F2 behaviour).
    /// </summary>
    public Orkeon.Application.Interfaces.Security.IPermissionGate? PermissionGate { get; init; }

    /// <summary>
    /// Optional host-native renderer for streamed <c>ctx.llm.act</c> deltas (F5 L3).
    /// Null = buffered behaviour unless the script passes <c>onDelta</c>.
    /// </summary>
    public Orkeon.Application.Interfaces.Ports.ILlmDeltaSink? DeltaSink { get; init; }

    /// <summary>
    /// Optional single tool-invocation point (GAP-09) a <c>ctx.llm.act</c> tool call goes
    /// through after the permission gate. Null = the tool is called directly.
    /// </summary>
    public Orkeon.Application.Interfaces.Security.IToolInvocationPipeline? ToolInvocation { get; init; }

    /// <summary>Optional durable store behind <c>ctx.memory.agent</c> (RT-16).</summary>
    public Orkeon.Application.Interfaces.Ports.IScriptAgentMemoryStore? AgentMemoryStore { get; init; }

    /// <summary>
    /// The configured tool-result cuts and fan-out bound of <c>ctx.llm.act</c>; a host sets it from
    /// <c>IOptions&lt;ToolResultLimitsOptions&gt;</c>, the defaults apply otherwise.
    /// </summary>
    public Orkeon.Domain.Tools.ToolResultLimitsOptions ToolLimits { get; init; } = new();
}
