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

    /// <summary>The three levels most effort fields know (OpenAI's <c>reasoning_effort</c>, Ollama's <c>think</c>).</summary>
    public static readonly IReadOnlyList<string> ThreeLevels = ["low", "medium", "high"];

    /// <summary>
    /// The level a provider whose field knows <paramref name="known"/> sends for <paramref name="asked"/>:
    /// the level itself when known, else the highest known level below it (the lowest known one when none
    /// is). The provider that lowers a level reports it (LLM-02): an option is never silently changed.
    /// </summary>
    public static string EffortFor(string asked, IReadOnlyList<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        if (known.Contains(asked))
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
}
