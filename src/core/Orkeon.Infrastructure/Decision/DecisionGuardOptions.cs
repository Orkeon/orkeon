namespace Orkeon.Infrastructure.Decision;

/// <summary>
/// The <c>Orkeon:Decision:Guard</c> section: the guardian that judges an agent's input with the
/// decision model, in the <c>Input</c> phase, beside the built-in input guard.
/// </summary>
public sealed class DecisionGuardOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Orkeon:Decision:Guard";

    /// <summary>Gets or sets whether the guard runs; off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets the risks detected, each with what it is; empty takes the default risks.</summary>
    public Dictionary<string, string> Risks { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the probability at or over which a risk blocks the input.</summary>
    public double BlockThreshold { get; set; } = 0.8;

    /// <summary>Gets or sets the probability at or over which a risk is a warning.</summary>
    public double WarnThreshold { get; set; } = 0.5;

    /// <summary>Gets or sets whether an unreachable decision model blocks the input; false warns and lets it in.</summary>
    public bool FailClosed { get; set; }
}
