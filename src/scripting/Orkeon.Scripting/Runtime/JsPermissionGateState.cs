namespace Orkeon.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase: read by scripts as ctx.llm.permissionGate.enabled

/// <summary>
/// The state of the host's permission gate as a script reads it, <c>ctx.llm.permissionGate</c> (#128):
/// read-only, so a script can tell whether the <c>permissionMode</c> it poses on <c>act</c> is checked.
/// </summary>
public sealed class JsPermissionGateState
{
    internal static readonly JsPermissionGateState On = new(true);
    internal static readonly JsPermissionGateState Off = new(false);

    private JsPermissionGateState(bool enabled) => this.enabled = enabled;

    /// <summary>Whether the host registered a permission gate that checks each tool call of <c>act</c>.</summary>
    public bool enabled { get; }
}
