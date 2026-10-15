using System.Collections.Immutable;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Puts a tool call the permission gate will not decide alone (<see cref="PermissionAction.Ask"/>) to an
/// operator and waits for the answer. A host registers one when someone can answer; without one, the gate
/// keeps its headless rule, where a call that needs an approval is refused.
/// </summary>
public interface IToolApprovalChannel
{
    /// <summary>Asks the operator about one tool call and waits for the answer.</summary>
    /// <param name="request">The call: the tool, its arguments, the mode and why the gate asks.</param>
    /// <param name="cancellationToken">Cancels the wait: the question is withdrawn and the call is not made.</param>
    /// <returns>The operator's decision.</returns>
    Task<ToolApprovalDecision> RequestAsync(ToolApprovalRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A tool call put to the operator by <see cref="IToolApprovalChannel"/>.</summary>
public sealed record ToolApprovalRequest
{
    /// <summary>The tool's registered name (e.g. <c>shell_command</c>).</summary>
    public required string ToolName { get; init; }

    /// <summary>The arguments the model supplied for the call.</summary>
    public required ImmutableDictionary<string, object?> Arguments { get; init; }

    /// <summary>The permission mode the gate applied.</summary>
    public required string Mode { get; init; }

    /// <summary>Why the gate asks, as it would tell the model.</summary>
    public required string Reason { get; init; }
}

/// <summary>The operator's answer to a <see cref="ToolApprovalRequest"/>.</summary>
public enum ToolApprovalDecision
{
    /// <summary>The call is refused; the model reads the refusal as the tool's result.</summary>
    Deny,

    /// <summary>This call runs.</summary>
    AllowOnce,

    /// <summary>This call runs, and the channel allows the tool without asking for the rest of the session.</summary>
    AllowForSession,
}
