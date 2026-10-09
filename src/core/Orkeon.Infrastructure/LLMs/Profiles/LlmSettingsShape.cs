using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// The model every call runs on unless a crew names a profile: the provider follows from
/// <c>BaseUrl</c>, then from the model's name, then from the shape of the key. Without this section a
/// host runs on the echo provider, and says so once.
/// </summary>
/// <remarks>
/// The keys of the <c>Llm</c> section, which <see cref="LlmSettings"/> reads raw (GAP-40): the
/// default profile's, its named profiles under <c>Profiles</c>, and <c>AvailableModels</c>, which the
/// session tools read. Declared by <c>AddOrkeonLlmProfiles</c>, so a key the section does not carry —
/// <c>Llm:Provider</c>, which nothing reads — refuses the host's start, naming it. A key
/// <see cref="LlmSettings"/> starts to read is added here. Nothing binds it: its properties are the
/// keys, and their values on a new instance the defaults <see cref="LlmSettings"/> and the provider
/// apply to a key left out, each named where it is defined.
/// </remarks>
internal class LlmSectionShape : LlmProfileShape
{
    /// <summary>
    /// The named profiles, each another provider with the keys of the section itself: a crew, an
    /// agent or a task picks one by its name.
    /// </summary>
    public Dictionary<string, LlmProfileShape>? Profiles { get; set; }

    /// <summary>The models a scripted <c>/model</c> may offer: a list, or one comma-separated value.</summary>
    public List<string>? AvailableModels { get; set; }
}

/// <summary>The keys of one profile of the <c>Llm</c> shape — the default section's own, a named profile's.</summary>
internal class LlmProfileShape
{
    /// <summary>The default of <see cref="Grammar"/>: no endpoint is taken to honour a grammar unless told.</summary>
    internal const bool DefaultGrammar = false;

    /// <summary>
    /// The address of the provider's endpoint, <c>http://</c> or <c>https://</c>. Left out, the
    /// address of the provider the model's name or the key points to.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API key, in clear text. Prefer <c>ApiKeyEnvVar</c>: a key written here is in the settings file.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The name of the environment variable that holds the API key, read when <c>ApiKey</c> resolves
    /// none: the process environment, then the Windows user scope.
    /// </summary>
    public string? ApiKeyEnvVar { get; set; }

    /// <summary>The model's name. Left out, the provider runs its own default model.</summary>
    public string? Model { get; set; }

    /// <summary>The sampling temperature. Left out, none is sent and the model applies its own.</summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// The most tokens an answer may hold: a pin. Left out, a request carries the documented maximum
    /// of its model, and 4096 for a model the catalogue does not know.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// How long one call may take, in seconds. Too short at its default for a model that thinks
    /// before it answers: write 600 for one, or turn its thinking off.
    /// </summary>
    public int? TimeoutSeconds { get; set; } = LlmDefaults.DefaultTimeoutSeconds;

    /// <summary>
    /// The longest silence a streamed answer may hold between two of its chunks, in seconds.
    /// Left out, nothing bounds it: <c>TimeoutSeconds</c> alone bounds the whole call, streamed
    /// or not. A model that thinks before it writes may stay silent for a while: set it only
    /// above that silence, or turn its thinking off.
    /// </summary>
    public int? StreamIdleSeconds { get; set; }

    /// <summary>How many times a call that fails on a passing error is tried again; <c>0</c> never retries.</summary>
    public int? MaxRetries { get; set; } = LlmDefaults.DefaultMaxRetries;

    /// <summary>What a provider that can think before it answers is asked to do.</summary>
    public LlmThinkingShape? Thinking { get; set; }

    /// <summary>
    /// Whether the endpoint honours a GBNF grammar — a llama.cpp-compatible server, such as Docker
    /// Model Runner. Elsewhere a grammar is dropped with a warning.
    /// </summary>
    public bool? Grammar { get; set; } = DefaultGrammar;
}

/// <summary>The keys of <c>Llm:Thinking</c>.</summary>
internal class LlmThinkingShape
{
    /// <summary>Whether the model thinks before it answers. Left out, the provider's own behaviour.</summary>
    public bool? Enabled { get; set; }

    /// <summary>How hard it thinks, in the provider's own words (<c>low</c>, <c>medium</c>, <c>high</c>…). Left out, the provider's own.</summary>
    public string? Effort { get; set; }
}
