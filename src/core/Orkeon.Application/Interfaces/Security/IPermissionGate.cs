using Orkeon.Domain.Tools;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Per-tool-call permission policy evaluated before each autonomous tool execution
/// (e.g. inside the scripted <c>ctx.llm.act</c> tool-calling loop). Implementations map a
/// permission mode (<c>default</c>, <c>acceptEdits</c>, <c>bypassPermissions</c>,
/// <c>plan</c>) to a verdict for the requested tool. A host that registers no gate keeps
/// the ungated behaviour.
/// </summary>
public interface IPermissionGate
{
    /// <summary>
    /// Evaluates whether <paramref name="toolName"/> may be invoked with
    /// <paramref name="arguments"/> under <paramref name="mode"/>.
    /// </summary>
    /// <param name="toolName">Registered tool name (e.g. <c>file_write</c>).</param>
    /// <param name="arguments">Arguments the model supplied for the call.</param>
    /// <param name="mode">Permission mode the session runs under.</param>
    /// <param name="declaredAccess">Access class the tool self-declares
    /// (<see cref="IBaseTool.Access"/>); <see cref="ToolAccess.Unspecified"/> when the tool
    /// declares nothing or could not be resolved — the gate then applies its own
    /// fail-closed classification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PermissionVerdict> CheckAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        string mode,
        ToolAccess declaredAccess = ToolAccess.Unspecified,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a permission check.</summary>
/// <param name="Action">The decision for this tool call.</param>
/// <param name="Message">Human/model-readable reason, set for <see cref="PermissionAction.Deny"/>
/// and <see cref="PermissionAction.Ask"/>.</param>
public sealed record PermissionVerdict(PermissionAction Action, string? Message = null)
{
    /// <summary>Allows the call.</summary>
    public static PermissionVerdict Allow() => new(PermissionAction.Allow);

    /// <summary>Denies the call with a reason.</summary>
    public static PermissionVerdict Deny(string message) => new(PermissionAction.Deny, message);

    /// <summary>Requires interactive approval (callers without a prompt channel treat this as deny).</summary>
    public static PermissionVerdict Ask(string message) => new(PermissionAction.Ask, message);
}

/// <summary>
/// Permission decision: <see cref="Allow"/> executes the tool, <see cref="Deny"/> feeds a
/// refusal back as the tool result, <see cref="Ask"/> requires interactive approval and
/// degrades to deny in non-interactive sessions.
/// </summary>
public enum PermissionAction
{
    /// <summary>Execute the tool call.</summary>
    Allow,
    /// <summary>Reject the tool call with a message.</summary>
    Deny,
    /// <summary>Prompt the user; deny when no interactive channel exists.</summary>
    Ask,
}
