namespace Orkeon.Domain.Agent;

/// <summary>
/// Fluent builder for constructing <see cref="GuardrailsConfig"/> instances.
/// Supports adding global rules, tool-specific clauses, and composing presets.
/// </summary>
/// <example>
/// <code>
/// var guardrails = new GuardrailsBuilder()
///     .UsePreset(GuardrailPresets.Analysis)
///     .AddRule("Always cite your sources")
///     .WhenUsing("file_write", "Only write to the output/ directory")
///     .WhenUsing("directory_read", "Limit recursive scans to project subdirectories")
///     .Build();
/// </code>
/// </example>
public sealed class GuardrailsBuilder
{
    private readonly List<string> _rules = [];
    private readonly Dictionary<string, List<string>> _toolRules = new(StringComparer.OrdinalIgnoreCase);
    private string? _header;
    private GuardrailsConfig? _basePreset;

    /// <summary>
    /// Sets a custom header text rendered before the rules section.
    /// </summary>
    public GuardrailsBuilder Header(string header)
    {
        _header = header;
        return this;
    }

    /// <summary>
    /// Adds a global rule that applies regardless of which tools the agent has.
    /// </summary>
    public GuardrailsBuilder AddRule(string rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        _rules.Add(rule);
        return this;
    }

    /// <summary>
    /// Adds multiple global rules at once.
    /// </summary>
    public GuardrailsBuilder AddRules(params string[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules)
            AddRule(rule);
        return this;
    }

    /// <summary>
    /// Adds a rule that only applies when the agent has access to the specified tool.
    /// Multiple calls for the same tool accumulate rules.
    /// </summary>
    /// <param name="toolName">Tool name (case-insensitive match).</param>
    /// <param name="rule">The constraint to enforce when this tool is available.</param>
    public GuardrailsBuilder WhenUsing(string toolName, string rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rule);

        if (!_toolRules.TryGetValue(toolName, out var list))
        {
            list = [];
            _toolRules[toolName] = list;
        }

        list.Add(rule);
        return this;
    }

    /// <summary>
    /// Adds multiple rules for a specific tool.
    /// </summary>
    public GuardrailsBuilder WhenUsing(string toolName, params string[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules)
            WhenUsing(toolName, rule);
        return this;
    }

    /// <summary>
    /// Layers a preset as the base configuration.
    /// Custom rules added via <see cref="AddRule"/> and <see cref="WhenUsing(string, string)"/>
    /// are merged on top of the preset.
    /// </summary>
    public GuardrailsBuilder UsePreset(GuardrailsConfig preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _basePreset = _basePreset != null ? _basePreset.MergeWith(preset) : preset;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="GuardrailsConfig"/> from accumulated rules and presets.
    /// </summary>
    public GuardrailsConfig Build()
    {
        var config = new GuardrailsConfig
        {
            Rules = _rules.ToList(),
            ToolRules = _toolRules.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value.ToList(),
                StringComparer.OrdinalIgnoreCase),
            Header = _header
        };

        return _basePreset != null
            ? _basePreset.MergeWith(config)
            : config;
    }
}
