namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Optional per-call "thinking mode" configuration for providers that distinguish a
/// reasoning pass from the visible response (DeepSeek V4, Anthropic extended thinking, …).
///
/// Surfaced by Experiment 07 friction #4 so a YAML crew can disable thinking on a
/// throwaway classification call or crank it to "max" for a planner agent without
/// changing C# wiring. Provider defaults apply when fields are left null.
/// </summary>
public sealed record LlmThinkingConfig
{
    /// <summary>Enables (<c>true</c>) or disables (<c>false</c>) the provider's thinking mode.</summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Reasoning effort hint forwarded as the provider's <c>reasoning_effort</c> field.
    /// The levels are <see cref="Levels"/>: <c>"low"</c>, <c>"medium"</c>, <c>"high"</c>, <c>"xhigh"</c>,
    /// <c>"max"</c>. A provider declares the ones its field knows
    /// (<see cref="LlmProviderCapabilities.EffortLevels"/>) and sends <see cref="EffortFor"/> for the others.
    /// </summary>
    public string? Effort { get; init; }

    private static readonly string[] s_levels = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>The effort levels a caller may ask for, lowest first.</summary>
    public static readonly IReadOnlyList<string> Levels = s_levels;

    /// <summary>
    /// The three levels a provider declares by default (<see cref="LlmProviderCapabilities.EffortLevels"/>):
    /// Ollama's <c>think</c>, and Gemini, Azure OpenAI and Mistral's <c>reasoning_effort</c> keep them.
    /// </summary>
    public static readonly IReadOnlyList<string> ThreeLevels = ["low", "medium", "high"];

    /// <summary>
    /// Returns <paramref name="effort"/> when it is exactly one of <see cref="Levels"/> (case-sensitive);
    /// throws <see cref="ArgumentException"/> otherwise, never falling back to a default level.
    /// </summary>
    public static string ValidateEffort(string effort)
    {
        if (effort is null || Array.IndexOf(s_levels, effort) < 0)
            throw new ArgumentException(
                $"Unknown thinking effort '{effort}': expected one of {string.Join(", ", s_levels)} (lower case).",
                nameof(effort));
        return effort;
    }

    /// <summary>
    /// The level a provider whose field knows <paramref name="known"/> sends for <paramref name="asked"/>:
    /// the level itself when known, else the highest known level below it (the lowest known one when none
    /// is). The provider that lowers a level reports it (LLM-02): an option is never silently changed.
    /// A value outside <see cref="Levels"/> (a vendor's own, such as OpenAI's <c>"none"</c>) is no level and is
    /// returned unchanged: the vendor judges it, rather than receiving a level the caller never asked for.
    /// </summary>
    public static string EffortFor(string asked, IReadOnlyList<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        if (known.Contains(asked) || Array.IndexOf(s_levels, asked) < 0)
            return asked;

        var rank = Array.IndexOf(s_levels, asked);
        var below = known
            .Where(level => Array.IndexOf(s_levels, level) < rank)
            .OrderByDescending(level => Array.IndexOf(s_levels, level))
            .FirstOrDefault();
        return below ?? known.OrderBy(level => Array.IndexOf(s_levels, level)).First();
    }

    /// <summary>
    /// Hard token budget for the reasoning pass, for the providers whose API accepts one
    /// (Qwen's <c>thinking_budget</c>).
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> mapped to Anthropic: the <c>budget_tokens</c> shape found in
    /// many older sources is rejected with an HTTP 400 by the current Claude generation, which
    /// takes <c>thinking: {type: "adaptive"}</c> and an effort level instead. Providers that
    /// declare less than <see cref="ThinkingSupport.Budget"/> report the option rather than
    /// dropping it.
    /// </remarks>
    public int? BudgetTokens { get; init; }

    /// <summary>
    /// The effort left to System One (<c>effort: auto</c>, #439), in place of <see cref="Effort"/>, which stays null:
    /// the decision model picks the level of each task. Null when the effort is written, or left to the profile.
    /// </summary>
    public LlmAutoEffort? AutoEffort { get; init; }

    /// <summary>Gets whether something of this config is left to System One (<see cref="AutoEffort"/>).</summary>
    public bool HasAuto => AutoEffort is not null;

    /// <summary>
    /// The thinking a task's calls run on once System One has settled what this config leaves to it (#439): with
    /// <paramref name="level"/>, that level in place of <c>auto</c>, the other fields as written; without one (a pick
    /// under the floor, no answer, the circuit off), the thinking the agent would have had without <c>auto</c> -
    /// <paramref name="standing"/>'s enabled switch and effort, this config's own switch and budget first. Null when
    /// nothing is left to send: the provider's own thinking applies. A config with nothing left to System One is
    /// returned as is.
    /// </summary>
    /// <param name="level">The level picked, one of <see cref="Levels"/>; null when none was.</param>
    /// <param name="standing">The thinking that stands without <c>auto</c>: the next config down the chain (the agent's under a task's, else the profile's); null for the provider's own.</param>
    /// <returns>A config with no <c>auto</c> left, or null.</returns>
    public LlmThinkingConfig? Settle(string? level, LlmThinkingConfig? standing)
    {
        if (!HasAuto)
            return this;
        if (level is not null)
            return this with { AutoEffort = null, Effort = ValidateEffort(level) };

        var settled = new LlmThinkingConfig
        {
            Enabled = Enabled ?? standing?.Enabled,
            Effort = standing?.AutoEffort is null ? standing?.Effort : null,
            BudgetTokens = BudgetTokens ?? standing?.BudgetTokens,
        };
        return settled is { Enabled: null, Effort: null, BudgetTokens: null } ? null : settled;
    }
}
