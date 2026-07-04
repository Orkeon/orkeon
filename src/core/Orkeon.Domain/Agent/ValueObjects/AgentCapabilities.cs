using System.Collections.Immutable;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>
/// Agent capabilities value object using immutable collections.
/// </summary>
public sealed record AgentCapabilities : ValueObjectRecord
{
    /// <summary>Gets the set of skills this agent possesses.</summary>
    public ImmutableHashSet<string> Skills { get; init; }
    /// <summary>Gets the set of tools this agent can use.</summary>
    public ImmutableHashSet<string> Tools { get; init; }
    /// <summary>Gets the set of languages this agent can communicate in.</summary>
    public ImmutableHashSet<string> Languages { get; init; }
    /// <summary>Gets the overall confidence level of this agent.</summary>
    public ConfidenceLevel OverallConfidence { get; init; }

    /// <summary>Creates a new <see cref="AgentCapabilities"/> instance.</summary>
    /// <param name="skills">Optional set of skills.</param>
    /// <param name="tools">Optional set of tools.</param>
    /// <param name="languages">Optional set of languages.</param>
    /// <param name="overallConfidence">Overall confidence level (default Medium).</param>
    /// <returns>A new <see cref="AgentCapabilities"/> instance.</returns>
    public static AgentCapabilities Create(
        IEnumerable<string>? skills = null,
        IEnumerable<string>? tools = null,
        IEnumerable<string>? languages = null,
        ConfidenceLevel? overallConfidence = null)
    {
        return new AgentCapabilities(skills, tools, languages, overallConfidence);
    }

    /// <summary>Initializes a new <see cref="AgentCapabilities"/> instance.</summary>
    /// <param name="skills">Optional set of skills.</param>
    /// <param name="tools">Optional set of tools.</param>
    /// <param name="languages">Optional set of languages.</param>
    /// <param name="overallConfidence">Overall confidence level (default Medium).</param>
    private AgentCapabilities(
        IEnumerable<string>? skills = null,
        IEnumerable<string>? tools = null,
        IEnumerable<string>? languages = null,
        ConfidenceLevel? overallConfidence = null)
    {
        Skills = (skills ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        Tools = (tools ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        Languages = (languages ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        OverallConfidence = overallConfidence ?? ConfidenceLevel.Medium;
    }

    /// <summary>Returns whether the agent has the specified skill.</summary>
    /// <param name="skill">The skill name.</param>
    /// <returns><see langword="true"/> if the agent has the skill; otherwise <see langword="false"/>.</returns>
    public bool HasSkill(string skill) => Skills.Contains(skill);
    /// <summary>Returns whether the agent has access to the specified tool.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns><see langword="true"/> if the agent has the tool; otherwise <see langword="false"/>.</returns>
    public bool HasTool(string tool) => Tools.Contains(tool);
    /// <summary>Returns whether the agent can communicate in the specified language.</summary>
    /// <param name="language">The language name.</param>
    /// <returns><see langword="true"/> if the agent has the language; otherwise <see langword="false"/>.</returns>
    public bool HasLanguage(string language) => Languages.Contains(language);

    /// <summary>Returns a new instance with the specified skill added.</summary>
    /// <param name="skill">The skill to add.</param>
    /// <returns>A new <see cref="AgentCapabilities"/> with the skill added.</returns>
    public AgentCapabilities AddSkill(string skill) => this with { Skills = Skills.Add(skill) };
    /// <summary>Returns a new instance with the specified tool added.</summary>
    /// <param name="tool">The tool to add.</param>
    /// <returns>A new <see cref="AgentCapabilities"/> with the tool added.</returns>
    public AgentCapabilities AddTool(string tool) => this with { Tools = Tools.Add(tool) };
    /// <summary>Returns a new instance with the specified language added.</summary>
    /// <param name="language">The language to add.</param>
    /// <returns>A new <see cref="AgentCapabilities"/> with the language added.</returns>
    public AgentCapabilities AddLanguage(string language) => this with { Languages = Languages.Add(language) };

    /// <summary>Returns a new instance with the specified skill removed.</summary>
    /// <param name="skill">The skill to remove.</param>
    /// <returns>A new <see cref="AgentCapabilities"/> with the skill removed.</returns>
    public AgentCapabilities RemoveSkill(string skill) => this with { Skills = Skills.Remove(skill) };
    /// <summary>Returns a new instance with the specified tool removed.</summary>
    /// <param name="tool">The tool to remove.</param>
    /// <returns>A new <see cref="AgentCapabilities"/> with the tool removed.</returns>
    public AgentCapabilities RemoveTool(string tool) => this with { Tools = Tools.Remove(tool) };

    private static readonly AgentCapabilities _empty = new();
    /// <summary>Gets an empty <see cref="AgentCapabilities"/> instance.</summary>
    public static AgentCapabilities Empty => _empty;

    /// <inheritdoc />
    public override string ToString() =>
        $"Skills: {Skills.Count}, Tools: {Tools.Count}, Languages: {Languages.Count}, Confidence: {OverallConfidence}";
}

/// <summary>Confidence level of an agent or capability.</summary>
public sealed record ConfidenceLevel
{
    /// <summary>Gets the string value of this confidence level.</summary>
    public string Value { get; }
    private ConfidenceLevel(string value) => Value = value;

    /// <summary>Low confidence level.</summary>
    public static readonly ConfidenceLevel Low = new("Low");
    /// <summary>Medium confidence level.</summary>
    public static readonly ConfidenceLevel Medium = new("Medium");
    /// <summary>High confidence level.</summary>
    public static readonly ConfidenceLevel High = new("High");
    /// <summary>Expert confidence level.</summary>
    public static readonly ConfidenceLevel Expert = new("Expert");

    private static readonly Dictionary<string, ConfidenceLevel> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Low)] = Low,
        [nameof(Medium)] = Medium,
        [nameof(High)] = High,
        [nameof(Expert)] = Expert,
    };

    /// <summary>Gets all known confidence levels.</summary>
    public static IReadOnlyCollection<ConfidenceLevel> All => s_all.Values;

    /// <summary>Returns the <see cref="ConfidenceLevel"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static ConfidenceLevel From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown ConfidenceLevel: '{value}'", nameof(value));

    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="ConfidenceLevel"/>.</summary>
    public static bool TryFrom(string? value, out ConfidenceLevel? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts a <see cref="ConfidenceLevel"/> to its string value.</summary>
    public static implicit operator string(ConfidenceLevel s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
