namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>
/// Read filter for <see cref="CommandInstanceRegistry.List"/> (design §6). Any field left
/// <see langword="null"/> is a wildcard. All supplied fields must match (logical AND).
/// </summary>
public sealed record CommandInstanceFilter(
    CommandInstanceState? State = null,
    string? Name = null,
    string? Agent = null)
{
    /// <summary>Matches everything.</summary>
    public static readonly CommandInstanceFilter All = new();

    /// <summary>True when <paramref name="instance"/> satisfies every supplied field.</summary>
    public bool Matches(CommandInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (State is { } s && instance.State != s) return false;
        if (Name is { } n && !string.Equals(n, instance.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (Agent is { } a && !string.Equals(a, instance.TargetAgent, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
