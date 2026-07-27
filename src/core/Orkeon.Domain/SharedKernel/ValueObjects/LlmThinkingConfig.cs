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
    /// Typical values: <c>"low"</c>, <c>"medium"</c>, <c>"high"</c>, <c>"max"</c>.
    /// </summary>
    public string? Effort { get; init; }

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
