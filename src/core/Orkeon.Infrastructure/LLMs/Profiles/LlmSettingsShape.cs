namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// The keys of the <c>Llm</c> section, which <see cref="LlmSettings"/> reads raw (GAP-40): the
/// default profile's, its named profiles under <c>Profiles</c>, and <c>AvailableModels</c>, which the
/// session tools read. Declared by <c>AddOrkeonLlmProfiles</c>, so a key the section does not carry —
/// <c>Llm:Provider</c>, which nothing reads: the provider follows from <c>BaseUrl</c> and the model —
/// refuses the host's start, naming it. A key <see cref="LlmSettings"/> starts to read is added here.
/// Never instantiated: its properties are the keys.
/// </summary>
internal abstract class LlmSectionShape : LlmProfileShape
{
    /// <summary>The named profiles, each of the profile shape.</summary>
    public Dictionary<string, LlmProfileShape>? Profiles { get; set; }

    /// <summary>The models a scripted <c>/model</c> may offer: a list, or one comma-separated value.</summary>
    public List<string>? AvailableModels { get; set; }
}

/// <summary>The keys of one profile of the <c>Llm</c> shape — the default section's own, a named profile's.</summary>
internal abstract class LlmProfileShape
{
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string? ApiKeyEnvVar { get; set; }

    public string? Model { get; set; }

    public double? Temperature { get; set; }

    public int? MaxTokens { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? MaxRetries { get; set; }

    public LlmThinkingShape? Thinking { get; set; }

    public bool? Grammar { get; set; }
}

/// <summary>The keys of <c>Llm:Thinking</c>.</summary>
internal abstract class LlmThinkingShape
{
    public bool? Enabled { get; set; }

    public string? Effort { get; set; }
}
