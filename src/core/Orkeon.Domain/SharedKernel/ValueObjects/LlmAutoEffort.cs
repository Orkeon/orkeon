namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// A thinking effort left to System One (<c>thinking: { effort: auto }</c>, #439): at the start of each task, the
/// decision model picks one of the levels the host allows (<c>Orkeon:Decision:Effort:Levels</c>), and the task keeps
/// it. Carried by <see cref="LlmThinkingConfig.AutoEffort"/> in place of a written level; it never reaches a provider.
/// </summary>
public sealed record LlmAutoEffort
{
    /// <summary>The spelling of the marker in a crew: <c>effort: auto</c>, compared case-insensitively.</summary>
    public const string Name = "auto";

    /// <summary>Gets the marker with no bound of its own: every level the host allows.</summary>
    public static LlmAutoEffort Any { get; } = new();

    /// <summary>Whether <paramref name="effort"/> is the marker <c>auto</c>.</summary>
    /// <param name="effort">An effort as written.</param>
    /// <returns>True for <c>auto</c>, whatever its case and surrounding blanks.</returns>
    public static bool IsAuto(string? effort) =>
        effort is not null && string.Equals(effort.Trim(), Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The levels the decision model may pick for this agent or task: the host's, in the host's order (cheapest
    /// first).
    /// </summary>
    /// <param name="hostLevels">The levels the host allows, each one of <see cref="LlmThinkingConfig.Levels"/>.</param>
    /// <returns>The allowed levels; empty when none is left.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "The bounds an agent or a task sets on the pick narrow the host's levels here: an instance member.")]
    public IReadOnlyList<string> Allowed(IReadOnlyList<string> hostLevels)
    {
        ArgumentNullException.ThrowIfNull(hostLevels);
        return hostLevels;
    }
}
