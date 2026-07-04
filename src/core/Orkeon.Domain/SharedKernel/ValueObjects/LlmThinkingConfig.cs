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
}
