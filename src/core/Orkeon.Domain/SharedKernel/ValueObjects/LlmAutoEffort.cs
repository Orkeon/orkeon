namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// A thinking effort left to System One (<c>thinking: { effort: auto }</c>, #439): at the start of each task, the
/// decision model picks one of the levels the host allows (<c>Orkeon:Decision:Effort:Levels</c>), and the task keeps
/// it. An agent or a task may bound the pick (<c>effort: { auto: true, min: low, max: high }</c>): the levels offered
/// are then the host's inside <see cref="Min"/> and <see cref="Max"/>. Carried by <see cref="LlmThinkingConfig.AutoEffort"/>
/// in place of a written level; it never reaches a provider.
/// </summary>
public sealed record LlmAutoEffort
{
    /// <summary>The spelling of the marker in a crew: <c>effort: auto</c>, compared case-insensitively.</summary>
    public const string Name = "auto";

    private LlmAutoEffort(string? min, string? max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Gets the marker with no bound of its own: every level the host allows.</summary>
    public static LlmAutoEffort Any { get; } = new(null, null);

    /// <summary>Gets the lowest level the pick may give, one of <see cref="LlmThinkingConfig.Levels"/>; null for no lower bound.</summary>
    public string? Min { get; }

    /// <summary>Gets the highest level the pick may give, one of <see cref="LlmThinkingConfig.Levels"/>; null for no upper bound.</summary>
    public string? Max { get; }

    /// <summary>Gets whether the pick is bounded (<see cref="Min"/> or <see cref="Max"/> set).</summary>
    public bool IsBounded => Min is not null || Max is not null;

    /// <summary>
    /// The marker bounded by <paramref name="min"/> and <paramref name="max"/> (#439); <see cref="Any"/> when neither is
    /// given.
    /// </summary>
    /// <param name="min">The lowest level the pick may give, one of <see cref="LlmThinkingConfig.Levels"/>; null for none.</param>
    /// <param name="max">The highest level the pick may give, one of <see cref="LlmThinkingConfig.Levels"/>; null for none.</param>
    /// <returns>The bounded marker.</returns>
    /// <exception cref="ArgumentException">A bound is not a level, or <paramref name="min"/> is above <paramref name="max"/>.</exception>
    public static LlmAutoEffort Bounded(string? min, string? max)
    {
        if (min is null && max is null)
            return Any;
        if (min is not null)
            LlmThinkingConfig.ValidateEffort(min);
        if (max is not null)
            LlmThinkingConfig.ValidateEffort(max);
        if (min is not null && max is not null && Rank(min) > Rank(max))
            throw new ArgumentException($"The lowest level '{min}' is above the highest '{max}': min is at most max.", nameof(min));
        return new LlmAutoEffort(min, max);
    }

    /// <summary>Whether <paramref name="effort"/> is the marker <c>auto</c>.</summary>
    /// <param name="effort">An effort as written.</param>
    /// <returns>True for <c>auto</c>, whatever its case and surrounding blanks.</returns>
    public static bool IsAuto(string? effort) =>
        effort is not null && string.Equals(effort.Trim(), Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The levels the decision model may pick for this agent or task: the host's inside <see cref="Min"/> and
    /// <see cref="Max"/>, in the host's order (cheapest first).
    /// </summary>
    /// <param name="hostLevels">The levels the host allows, each one of <see cref="LlmThinkingConfig.Levels"/>.</param>
    /// <returns>The allowed levels; empty when none is left.</returns>
    public IReadOnlyList<string> Allowed(IReadOnlyList<string> hostLevels)
    {
        ArgumentNullException.ThrowIfNull(hostLevels);
        if (!IsBounded)
            return hostLevels;

        var low = Min is null ? int.MinValue : Rank(Min);
        var high = Max is null ? int.MaxValue : Rank(Max);
        return [.. hostLevels.Where(level => Rank(level) >= low && Rank(level) <= high)];
    }

    /// <summary>The bounds as a crew writes them (<c>min: low, max: high</c>); empty when the pick is not bounded.</summary>
    /// <returns>The bounds, comma-separated.</returns>
    public string DescribeBounds() =>
        string.Join(", ", new[] { Min is null ? null : $"min: {Min}", Max is null ? null : $"max: {Max}" }.Where(b => b is not null));

    private static int Rank(string level) => LlmThinkingConfig.RankOf(level);
}
