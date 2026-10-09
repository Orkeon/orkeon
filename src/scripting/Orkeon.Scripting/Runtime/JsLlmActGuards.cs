namespace Orkeon.Scripting.Runtime;

/// <summary>
/// What a <see cref="JsLlmFacade"/> checks before <c>act</c> runs a tool: the run's budget, the
/// host's permission gate, and the agent's tools the host does not offer. Bundled to keep the
/// facade constructor readable; every member stays optional, and an absent bundle means nothing
/// is checked, which is the behaviour of a host that wired none of them.
/// </summary>
internal sealed record JsLlmActGuards
{
    /// <summary>The run's budget, when it has one.</summary>
    public Orkeon.Domain.Autonomous.AgentExecutionBudget? Budget { get; init; }

    /// <summary>The host's permission gate, when it registered one.</summary>
    public Orkeon.Application.Interfaces.Security.IPermissionGate? PermissionGate { get; init; }

    /// <summary>
    /// The names of the agent's <c>.tools([...])</c> the host does not offer: <c>act</c> then
    /// rejects with an <see cref="Orkeon.Scripting.Exceptions.UnknownToolException"/> before any model call (GAP-27).
    /// </summary>
    public UnknownAgentTools? UnknownTools { get; init; }
}
