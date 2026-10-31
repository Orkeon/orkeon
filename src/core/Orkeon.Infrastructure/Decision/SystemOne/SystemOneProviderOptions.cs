namespace Orkeon.Infrastructure.Decision.SystemOne;

/// <summary>How <see cref="SystemOneDecisionModel"/> reaches a System One provider.</summary>
public sealed class SystemOneProviderOptions
{
    /// <summary>Gets or sets the API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the environment variable read when <see cref="ApiKey"/> is empty.</summary>
    public string ApiKeyEnvVar { get; set; } = string.Empty;

    /// <summary>Gets or sets the API root.</summary>
    public Uri BaseUrl { get; set; } = null!;

    /// <summary>Gets or sets the default model.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the HTTP timeout of one attempt, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the retries.</summary>
    public int MaxRetries { get; set; } = 3;
}
