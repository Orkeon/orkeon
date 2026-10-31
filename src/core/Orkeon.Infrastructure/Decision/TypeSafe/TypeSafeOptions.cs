using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.Decision.TypeSafe;

/// <summary>
/// The <c>Orkeon:TypeSafe</c> section: how <see cref="TypeSafeDecisionModel"/> reaches the
/// TypeSafe System One API.
/// </summary>
public sealed class TypeSafeOptions
{
    /// <summary>The configuration section bound to these options.</summary>
    public const string SectionName = "Orkeon:TypeSafe";

    /// <summary>
    /// Gets or sets the API key. Discouraged in a settings file: leave it empty and set the
    /// variable <see cref="ApiKeyEnvVar"/> names.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the environment variable read when <see cref="ApiKey"/> is empty.</summary>
    public string ApiKeyEnvVar { get; set; } = DecisionProviderDefaults.TypeSafeApiKeyVariable;

    /// <summary>Gets or sets the API root; <c>/systemone</c> is appended to it.</summary>
    public Uri BaseUrl { get; set; } = new(DecisionProviderDefaults.TypeSafeBaseUrl);

    /// <summary>Gets or sets the model a request naming none runs on.</summary>
    public string Model { get; set; } = DecisionProviderDefaults.TypeSafeModel;

    /// <summary>Gets or sets the HTTP timeout of one attempt, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the retries after a 429, a 529, another 5xx or a network error.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Gets the agent input guard (<c>Orkeon:TypeSafe:Guard</c>).</summary>
    public TypeSafeGuardOptions Guard { get; init; } = new();
}

/// <summary>
/// The <c>Orkeon:TypeSafe:Guard</c> section: the guardian that judges an agent's input with the
/// decision model, in the <c>Input</c> phase, beside the built-in input guard.
/// </summary>
public sealed class TypeSafeGuardOptions
{
    /// <summary>Gets or sets whether the guard runs; off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets the risks detected, each with what it is; empty takes
    /// <see cref="TypeSafeInputGuard.DefaultRisks"/>.
    /// </summary>
    public Dictionary<string, string> Risks { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the probability at or over which a risk blocks the input.</summary>
    public double BlockThreshold { get; set; } = 0.8;

    /// <summary>Gets or sets the probability at or over which a risk is a warning.</summary>
    public double WarnThreshold { get; set; } = 0.5;

    /// <summary>Gets or sets whether an unreachable decision model blocks the input; false warns and lets it in.</summary>
    public bool FailClosed { get; set; }
}
