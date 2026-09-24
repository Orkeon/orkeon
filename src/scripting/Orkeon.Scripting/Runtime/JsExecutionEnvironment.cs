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
}
