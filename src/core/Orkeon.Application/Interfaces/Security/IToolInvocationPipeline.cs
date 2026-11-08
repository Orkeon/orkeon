using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>Who makes a tool call: the agent of the turn, its task and its crew.</summary>
/// <param name="AgentId">The calling agent's id.</param>
/// <param name="AgentRole">The calling agent's role.</param>
/// <param name="TaskId">The task the call serves, when known.</param>
/// <param name="CrewId">The crew the call runs in, when known.</param>
public sealed record ToolInvocationCaller(string AgentId, string AgentRole, string? TaskId = null, string? CrewId = null)
{
    /// <summary>
    /// Gets the wire name of the permission mode the call runs under (<c>default</c>, <c>acceptEdits</c>, ...):
    /// the agent's <c>permissionMode</c>, or the one a script poses on <c>act</c>. Null (the agent declares
    /// none) means <c>Orkeon:Security:PermissionGate:DefaultMode</c>.
    /// </summary>
    public string? PermissionMode { get; init; }

    /// <summary>
    /// Gets a value indicating whether the call is not put to the permission gate: the caller is the
    /// operator's own (<c>orkeon mcp serve</c>, the forge), not an agent acting on its own.
    /// </summary>
    public bool GateExempt { get; init; }

    /// <summary>
    /// Gets a value indicating whether the call must be approved by an operator: the pipeline asks the gate as a
    /// <c>default</c>-mode edit. It never relaxes a stricter mode: under <c>plan</c> or <c>dontAsk</c> the call is
    /// first put to the gate in that mode, and refused when that mode refuses it. A script's <c>onToolCall</c>
    /// that answers <c>ask</c> sets it.
    /// </summary>
    public bool RequireApproval { get; init; }

    /// <summary>
    /// Gets what the caller does while the pipeline waits for the gate and an operator's answer: it returns a
    /// scope the pipeline holds around both and disposes after. A script stops its clock and its agent's
    /// wall-time budget there, since that wait is not the script's. Null holds nothing.
    /// </summary>
    public Func<IDisposable>? WaitScope { get; init; }

    /// <summary>
    /// Gets what the caller does once the guardian and the permission gate let the call through, before the tool
    /// runs: a script charges its tool-call budget there, so a refused call costs nothing. What it throws ends the
    /// call, the tool unrun. Null does nothing.
    /// </summary>
    public Action? Admitted { get; init; }
}

/// <summary>One tool call an agent turn asks for.</summary>
/// <param name="Tool">The tool to call.</param>
/// <param name="Arguments">The arguments the model supplied.</param>
/// <param name="Caller">Who makes the call.</param>
public sealed record ToolInvocation(IBaseTool Tool, Dictionary<string, object?> Arguments, ToolInvocationCaller Caller);

/// <summary>What became of a tool call that went through <see cref="IToolInvocationPipeline"/>.</summary>
public sealed record ToolInvocationResult
{
    /// <summary>Gets the text fed back to the model: truncated, and tagged as data when the call succeeded.</summary>
    public required string ConversationText { get; init; }

    /// <summary>
    /// Gets the tool's own text, before truncation and tagging — <c>Error: …</c> on a failure —
    /// for the usage record and the logs.
    /// </summary>
    public required string RawText { get; init; }

    /// <summary>Gets a value indicating whether the tool ran and reported success.</summary>
    public bool Success { get; init; }

    /// <summary>Gets a value indicating whether the guardian blocked the call before the tool ran.</summary>
    public bool Blocked { get; init; }

    /// <summary>Gets the tool's response, null when the call was blocked.</summary>
    public ToolCallResponse? Response { get; init; }
}

/// <summary>
/// The single point every agent turn calls a tool through (GAP-09) — the chat-client,
/// native, text and streaming loops alike. It chains: the guardian's
/// <see cref="GuardPhase.ToolExecution"/> phase (or <see cref="GuardPhase.Delegation"/> for
/// the delegation tools) → the call → the one truncation rule → <see cref="IToolResultSanitizer"/>
/// → a <c>ToolExecution</c> audit event. A decorator could not do this: it never sees the
/// tools registered at run time (MCP) nor the ones built per agent.
/// </summary>
public interface IToolInvocationPipeline
{
    /// <summary>
    /// Runs <paramref name="invocation"/>. A blocked call does not reach the tool and returns
    /// <see cref="ToolInvocationResult.Blocked"/> with an <c>Error: …</c> text the model can read;
    /// an exception thrown by the tool is audited and rethrown.
    /// </summary>
    System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value indicating whether this pipeline puts the calls of an agent to a permission gate. A caller
    /// that holds a gate of its own (the script facade) leaves the question to the pipeline only when this is
    /// true: a pipeline that does not gate would let the call through unchecked. A decorator answers for the
    /// pipeline it wraps.
    /// </summary>
    bool GatesPermissions => false;
}
