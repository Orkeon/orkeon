namespace Orkeon.Domain.Agent;

/// <summary>
/// Configurable guardrails injected into agent system prompts to constrain LLM behavior.
/// Supports global rules, tool-specific clauses, and composable presets.
/// </summary>
public sealed record GuardrailsConfig
{
    /// <summary>
    /// Global rules applied to the agent regardless of which tools are available.
    /// Each string is rendered as a numbered instruction in the system prompt.
    /// </summary>
    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>
    /// Tool-specific rules: key = tool name (case-insensitive), value = list of rules
    /// that are only included when the agent has access to the named tool.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ToolRules { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional header text rendered before the rules section.
    /// Defaults to "OPERATIONAL RULES — You MUST follow these at all times:" when null.
    /// </summary>
    public string? Header { get; init; }

    /// <summary>Returns true if this config contains no rules at all.</summary>
    public bool IsEmpty => Rules.Count == 0 && ToolRules.Count == 0;

    /// <summary>
    /// Merges two guardrail configs, concatenating rules and combining tool rules.
    /// Useful for layering a preset with custom additions.
    /// </summary>
    public GuardrailsConfig MergeWith(GuardrailsConfig other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var mergedRules = new List<string>(Rules);
        mergedRules.AddRange(other.Rules);

        var mergedToolRules = new Dictionary<string, IReadOnlyList<string>>(
            ToolRules, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in other.ToolRules)
        {
            if (mergedToolRules.TryGetValue(kvp.Key, out var existing))
            {
                var combined = new List<string>(existing);
                combined.AddRange(kvp.Value);
                mergedToolRules[kvp.Key] = combined;
            }
            else
            {
                mergedToolRules[kvp.Key] = kvp.Value;
            }
        }

        return new GuardrailsConfig
        {
            Rules = mergedRules,
            ToolRules = mergedToolRules,
            Header = Header ?? other.Header
        };
    }
}
