namespace Orkeon.Infrastructure.LLMs.Decision;

/// <summary>
/// The <c>Orkeon:Decision</c> section: the decision profiles, each a set of stages run around the
/// calls to a target profile. A crew uses one like any profile, <c>llm: { profile: &lt;name&gt; }</c>.
/// </summary>
public sealed class DecisionProfilesOptions
{
    /// <summary>The configuration section bound to these options.</summary>
    public const string SectionName = "Orkeon:Decision";

    /// <summary>Gets the decision profiles by name.</summary>
    public Dictionary<string, DecisionProfileOptions> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The <c>Orkeon:Decision:Enabled</c> switch of the decision circuit.</summary>
public sealed class DecisionCircuitOptions
{
    /// <summary>The configuration section bound to these options.</summary>
    public const string SectionName = "Orkeon:Decision";

    /// <summary>Gets or sets a value indicating whether decision tasks ask the decision model.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>One decision profile: <c>Orkeon:Decision:Profiles:&lt;name&gt;</c>.</summary>
public sealed class DecisionProfileOptions
{
    /// <summary>Gets or sets the profile a call goes to when no stage routes it; <c>default</c> is the <c>Llm</c> section.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Gets or sets whether a failing stage blocks the call; false lets it go on with a warning.</summary>
    public bool FailClosed { get; set; }

    /// <summary>Gets the stages, run in order.</summary>
    public IList<DecisionStageOptions> Stages { get; init; } = [];
}

/// <summary>One stage of a decision profile.</summary>
public sealed class DecisionStageOptions
{
    /// <summary>Gets or sets the stage: <c>route</c>, <c>guard</c> or <c>verify</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets when a <c>guard</c> judges: <c>request</c>, <c>response</c> or <c>both</c> (the default).</summary>
    public string? Phase { get; set; }

    /// <summary>Gets the profiles a <c>route</c> may choose, each with what it is for.</summary>
    public Dictionary<string, string> Routes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the confidence under which a <c>route</c> takes <see cref="Fallback"/>.</summary>
    public double MinConfidence { get; set; }

    /// <summary>Gets or sets the profile a <c>route</c> takes under <see cref="MinConfidence"/>; empty keeps the target.</summary>
    public string? Fallback { get; set; }

    /// <summary>Gets the risks a <c>guard</c> detects, each with what it is.</summary>
    public Dictionary<string, string> Risks { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the probability at or over which a <c>guard</c> blocks.</summary>
    public double BlockThreshold { get; set; } = 0.5;

    /// <summary>Gets or sets the probability at or over which a <c>guard</c> logs a warning.</summary>
    public double WarnThreshold { get; set; } = 0.3;

    /// <summary>Gets or sets the profile a <c>verify</c> escalates to.</summary>
    public string? EscalateTo { get; set; }

    /// <summary>Gets or sets the probability under which a <c>verify</c> escalates.</summary>
    public double MinProbability { get; set; } = 0.5;

    /// <summary>Gets or sets what a good answer is, for a <c>verify</c>; empty takes the default criterion.</summary>
    public string? Criterion { get; set; }
}
