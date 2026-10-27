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

    /// <summary>Gets or sets the API root; <c>/systemone</c> and <c>/models</c> are appended to it.</summary>
    public Uri BaseUrl { get; set; } = new(DecisionProviderDefaults.TypeSafeBaseUrl);

    /// <summary>Gets or sets the model a request naming none runs on.</summary>
    public string Model { get; set; } = DecisionProviderDefaults.TypeSafeModel;

    /// <summary>Gets or sets the HTTP timeout of one attempt, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the retries after a 429, a 529, another 5xx or a network error.</summary>
    public int MaxRetries { get; set; } = 3;
}
