namespace Orkeon.Domain.Security;

/// <summary>
/// The permission mode an agent runs its tool calls under, as the permission gate reads it. The gate maps
/// each mode to a verdict per tool; the names on the wire are the ones a script and a YAML crew write
/// (<see cref="PermissionModes.ToWireName"/>).
/// </summary>
public enum PermissionMode
{
    /// <summary>Reads pass; every other tool asks for an approval.</summary>
    Default,

    /// <summary>Reads and file edits pass; shell commands ask.</summary>
    AcceptEdits,

    /// <summary>Every call passes.</summary>
    BypassPermissions,

    /// <summary>Reads pass; everything else is denied: a turn that changes nothing.</summary>
    Plan,

    /// <summary>Reads pass; everything else is denied, never asked.</summary>
    DontAsk,
}

/// <summary>The wire names of <see cref="PermissionMode"/>: <c>default</c>, <c>acceptEdits</c>, <c>bypassPermissions</c>, <c>plan</c>, <c>dontAsk</c>.</summary>
public static class PermissionModes
{
    /// <summary>The name the gate and the YAML key <c>permissionMode</c> use for <paramref name="mode"/>.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Its camelCase name.</returns>
    public static string ToWireName(this PermissionMode mode) => mode switch
    {
        PermissionMode.Default => "default",
        PermissionMode.AcceptEdits => "acceptEdits",
        PermissionMode.BypassPermissions => "bypassPermissions",
        PermissionMode.Plan => "plan",
        PermissionMode.DontAsk => "dontAsk",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown permission mode."),
    };

    /// <summary>
    /// The stricter of <paramref name="own"/> and <paramref name="inherited"/>: plan and dontAsk (reads
    /// only) above default (asks), above acceptEdits, above bypassPermissions. A delegated or spawned run
    /// is held to it, so a child never loosens the mode of the run that started it.
    /// </summary>
    public static PermissionMode Stricter(this PermissionMode own, PermissionMode? inherited) =>
        inherited is { } outer && Strictness(outer) > Strictness(own) ? outer : own;

    private static int Strictness(PermissionMode mode) => mode switch
    {
        PermissionMode.BypassPermissions => 0,
        PermissionMode.AcceptEdits => 1,
        PermissionMode.Default => 2,
        _ => 3,
    };

    /// <summary>Reads a wire name, ignoring case.</summary>
    /// <param name="name">The name, e.g. <c>acceptEdits</c>.</param>
    /// <param name="mode">The mode, when the name is one.</param>
    /// <returns>Whether <paramref name="name"/> names a mode.</returns>
    public static bool TryParse(string? name, out PermissionMode mode)
    {
        var trimmed = name?.Trim();
        var modes = Enum.GetValues<PermissionMode>();
        var index = Array.FindIndex(modes, c => string.Equals(c.ToWireName(), trimmed, StringComparison.OrdinalIgnoreCase));
        mode = index >= 0 ? modes[index] : PermissionMode.Default;
        return index >= 0;
    }
}
