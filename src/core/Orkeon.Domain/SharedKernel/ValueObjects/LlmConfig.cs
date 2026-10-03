using Orkeon.Constants.Llm;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Configuration for Language Model settings.</summary>
/// <remarks>
/// The same type describes two things: the configuration a provider is built with (the host's
/// <c>Llm</c> section, a profile) and the configuration a caller passes with one call. A call's
/// configuration <b>completes</b> its provider's (<see cref="InheritFrom"/>, GAP-29): each field the
/// call leaves unset takes the provider's value, each field it sets wins.
/// </remarks>
public sealed record LlmConfig
{
    /// <summary>Default base URL for the Anthropic (Claude) API.</summary>
    private const string AnthropicBaseUrl = LlmProviderEndpoints.Anthropic;

    /// <summary>Default base URL for a locally running Ollama server.</summary>
    private const string OllamaBaseUrl = LlmProviderEndpoints.OllamaDefault;

    /// <summary>
    /// Gets the model identifier (e.g., "gpt-4"). Empty — the default — on a configuration that
    /// names no model (<see cref="OnProfile"/>): the call then runs on the model of the provider it
    /// reaches, the one its profile configures, else that provider's own default (GAP-18). Never a
    /// vendor's model pinned for every vendor.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// The host's named LLM profile this configuration runs on (<c>Llm:Profiles:&lt;name&gt;</c>),
    /// or null for the host's default profile — the <c>Llm</c> section (GAP-17). A crew names a
    /// profile, never a key or an endpoint: those stay in the host's configuration.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    /// The API key the provider authenticates with. This is the supported path in 1.0:
    /// every provider guard and every request path reads this property, and a config
    /// without it is treated as unconfigured.
    /// </summary>
    /// <remarks>
    /// Keep the value out of source and out of crew YAML: bind it from the environment
    /// (<c>ORKEON_Llm__ApiKey</c>), or name the variable that holds it in the settings file
    /// (<c>Llm:ApiKeyEnvVar</c>, and <c>Llm:Profiles:&lt;name&gt;:ApiKeyEnvVar</c> per profile),
    /// which is what the runners read — a key the configuration resolves first, then the named
    /// variable, in the process environment and then the user's on Windows (STUDIO-49).
    /// <c>Llm:ApiKey</c> in clear text in the file works, and is discouraged.
    /// </remarks>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Logical name of a secret, recorded for a host that resolves secrets itself.
    /// </summary>
    /// <remarks>
    /// <b>Reserved, and not resolved by the framework in 1.0.</b> Nothing in Orkeon turns
    /// this name into a key: a config that carries only <see cref="ApiKeySecretName"/> is
    /// unconfigured, and every call against it answers that an API key is required. A host
    /// that keeps its keys in a secret store resolves the name itself and assigns the
    /// result to <see cref="ApiKey"/>. See <c>docs/reference/limitations.md</c>.
    /// </remarks>
    public string? ApiKeySecretName { get; init; }

    /// <summary>Gets the base URL for the LLM provider.</summary>
    public Uri? BaseUrl { get; init; }
    /// <summary>Gets the sampling temperature (0.0 to 1.0).</summary>
    public double Temperature { get; init; } = LlmDefaults.DefaultTemperature;
    /// <summary>
    /// The output cap pinned on this configuration, or null when nothing pins one — the
    /// request then carries the model's documented maximum, and 4096 only for a model the
    /// catalogue does not know (LLM-10, <see cref="ResolveMaxTokens"/>). Set from
    /// <c>Llm:MaxTokens</c>, a Studio profile or a crew's <c>max_tokens</c>; never defaulted
    /// here, so "pinned" and "left to the model" stay distinguishable all the way to the wire.
    /// </summary>
    public int? MaxTokens { get; init; }
    /// <summary>Gets the nucleus sampling probability.</summary>
    public double TopP { get; init; } = 1.0;
    /// <summary>Gets the frequency penalty.</summary>
    public double FrequencyPenalty { get; init; }
    /// <summary>Gets the presence penalty.</summary>
    public double PresencePenalty { get; init; }
    /// <summary>Gets the random seed for reproducible outputs, or null for random.</summary>
    public int? Seed { get; init; }
    /// <summary>Gets the stop sequences that terminate generation.</summary>
    public IReadOnlyList<string> StopSequences { get; init; } = [];

    /// <summary>
    /// Optional system message to prepend to conversations.
    /// Typed alternative to <c>CustomParameters["system_message"]</c>.
    /// </summary>
    public string? SystemMessage { get; init; }

    /// <summary>
    /// API version override (e.g. for Azure OpenAI).
    /// Typed alternative to <c>CustomParameters["api_version"]</c>.
    /// </summary>
    public string? ApiVersion { get; init; }

    /// <summary>
    /// Workspace the requests act in, for providers whose keys are workspace-scoped.
    /// Anthropic's identity-linked API keys refuse every request that does not carry an
    /// <c>anthropic-workspace-id</c> header (measured live, 2026-08-30), so without this
    /// field such a key cannot be used at all. Not a secret — it scopes the key, it does
    /// not authenticate. Ignored by providers that have no workspace concept, the same
    /// way <see cref="ApiVersion"/> is Azure's own out-of-band detail.
    /// </summary>
    public string? WorkspaceId { get; init; }

    /// <summary>
    /// Additional provider-specific parameters.
    /// Prefer the typed properties (<see cref="SystemMessage"/>, <see cref="ApiVersion"/>)
    /// for well-known keys. Use this dictionary only for provider-specific extensions.
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomParameters { get; init; } = new Dictionary<string, object>();
    /// <summary>
    /// The request timeout in seconds, or null when nothing pins one: a call's configuration then
    /// runs on its provider's timeout (<c>Llm:TimeoutSeconds</c>, 600 s for a model that thinks
    /// before it answers), and a provider configured without one on
    /// <see cref="LlmDefaults.DefaultTimeoutSeconds"/> (<see cref="ResolveTimeoutSeconds"/>). It used
    /// to default to 30, so a caller's configuration could not tell "not set" from "30" and every
    /// call outside the chat client ran on 30 s whatever the host configured (GAP-29).
    /// </summary>
    public int? TimeoutSeconds { get; init; }
    /// <summary>
    /// Gets the maximum number of retries on transient failures (bound from <c>Llm:MaxRetries</c>).
    /// Drives the HTTP resilience policy of the buffered path and the connect-phase retry
    /// budget of the streaming path.
    /// </summary>
    public int MaxRetries { get; init; } = LlmDefaults.DefaultMaxRetries;

    /// <summary>Gets the tool schemas to include in the LLM request payload.</summary>
    public IReadOnlyList<ToolSchema>? Tools { get; init; }

    /// <summary>Gets the tool calling mode (Auto, Required, None).</summary>
    public ToolCallMode ToolMode { get; init; } = ToolCallMode.Auto;

    /// <summary>
    /// Optional GBNF grammar string to constrain generation, set by a <c>structured_output</c>
    /// deliverable. Forwarded as the top-level <c>grammar</c> field only when the endpoint takes
    /// one (<see cref="GrammarEnabled"/>, a llama.cpp-compatible server); any other provider
    /// drops it with a warning that names <c>Llm:Grammar</c>.
    /// </summary>
    public string? GrammarGbnf { get; init; }

    /// <summary>
    /// Whether the endpoint this configuration points at honours a GBNF <c>grammar</c> field —
    /// <c>llama-server</c> or Docker Model Runner behind the OpenAI provider, a llama.cpp build
    /// behind Ollama. Bound from <c>Llm:Grammar</c>, <see langword="false"/> by default: no vendor
    /// API documents the field. Read once, from the configuration a provider is built with, into
    /// <see cref="LlmProviderCapabilities.GbnfGrammar"/>.
    /// </summary>
    public bool GrammarEnabled { get; init; }

    /// <summary>
    /// Optional thinking-mode toggle and reasoning-effort hint. Honored by providers that
    /// expose a thinking/reasoning track (DeepSeek V4, …). <c>null</c> = leave the provider
    /// default in place (e.g. DeepSeek thinking is enabled by default with effort=high).
    /// </summary>
    public LlmThinkingConfig? Thinking { get; init; }

    /// <summary>
    /// Optional prompt-cache request, for the providers whose cache must be marked explicitly
    /// (Anthropic). <c>null</c> = no breakpoint, which on those providers means no caching at
    /// all. Providers that cache implicitly ignore it.
    /// </summary>
    public LlmCacheConfig? Cache { get; init; }

    /// <summary>
    /// Optional output-format constraint forwarded to OpenAI-compatible providers that
    /// implement the <c>response_format</c> field (DeepSeek today; OpenAI/Grok can opt in
    /// later). <c>null</c> = provider default (free text).
    /// </summary>
    public LlmResponseFormat? ResponseFormat { get; init; }

    /// <summary>Initializes a new instance of <see cref="LlmConfig"/> that names no model, with default settings.</summary>
    private LlmConfig() { }

    /// <summary>Initializes a new instance of <see cref="LlmConfig"/> with a model and optional API key.</summary>
    /// <param name="model">The model identifier.</param>
    /// <param name="apiKey">The API key (deprecated; prefer secret-based resolution).</param>
    private LlmConfig(string model, string? apiKey = null)
    {
        Model = model;
        ApiKey = apiKey;
    }

    /// <summary>
    /// The output cap a request carries, or null when the field must be left out. Three
    /// sources, in order: the value pinned on this config (any positive <see cref="MaxTokens"/>);
    /// the model's documented maximum from <see cref="LlmModelOutputLimits"/> (on this
    /// provider first, then by model id); the engine fallback
    /// <see cref="LlmDefaults.FallbackMaxOutputTokens"/> for a model the catalogue does not
    /// know. A model the vendor documents as unbounded yields null: the endpoint then
    /// generates up to its window.
    /// </summary>
    /// <param name="provider">The provider key, for a catalogue entry that only holds on that endpoint.</param>
    /// <param name="defaultModel">The model the provider sends when this config names none (empty or blank).</param>
    public int? ResolveMaxTokens(string? provider = null, string? defaultModel = null)
    {
        if (MaxTokens is > 0)
            return MaxTokens;

        return LlmModelOutputLimits.MaxOutputTokens(string.IsNullOrWhiteSpace(Model) ? defaultModel : Model, provider) switch
        {
            null => LlmDefaults.FallbackMaxOutputTokens,
            LlmModelOutputLimits.Unbounded => null,
            var documented => documented,
        };
    }

    /// <summary>
    /// The timeout a request runs on, in seconds: the one this configuration pins, else
    /// <see cref="LlmDefaults.DefaultTimeoutSeconds"/>.
    /// </summary>
    /// <returns>A timeout in seconds.</returns>
    public int ResolveTimeoutSeconds() => TimeoutSeconds ?? LlmDefaults.DefaultTimeoutSeconds;

    /// <summary>
    /// This configuration — a call's — completed by <paramref name="provider"/>, the configuration
    /// the provider it reaches was built with (GAP-29). Every field this configuration leaves unset
    /// takes the provider's value: a null, an empty or blank string — model, key, workspace, API
    /// version —, no stop sequence. Every field it sets wins. Custom parameters merge, this
    /// configuration's keys winning. The settings that have no unset value — the temperature, the
    /// nucleus and penalty settings, the tool mode — are the caller's: a caller that builds a
    /// configuration owns its sampling. <see cref="MaxRetries"/> and <see cref="GrammarEnabled"/>
    /// are read from the provider's own configuration when it is built, never from a call.
    /// </summary>
    /// <remarks>
    /// The providers took a call's configuration whole (<c>config ?? Config</c>), so every caller
    /// outside the chat client — the planner, the cognitive memory, the context-window and
    /// RaggableTree summarizers, the agent loops — lost the key, the base URL, the timeout and the
    /// provider's own settings. A call cannot unset what its provider sets; it overrides it —
    /// <c>Thinking = { Enabled = false }</c>, <see cref="LlmResponseFormat.Text"/>.
    /// </remarks>
    /// <param name="provider">The configuration the provider was built with.</param>
    /// <returns>The configuration the call runs on.</returns>
    public LlmConfig InheritFrom(LlmConfig provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return this with
        {
            Model = string.IsNullOrWhiteSpace(Model) ? provider.Model : Model,
            Profile = string.IsNullOrWhiteSpace(Profile) ? provider.Profile : Profile,
            ApiKey = string.IsNullOrEmpty(ApiKey) ? provider.ApiKey : ApiKey,
            ApiKeySecretName = string.IsNullOrWhiteSpace(ApiKeySecretName) ? provider.ApiKeySecretName : ApiKeySecretName,
            BaseUrl = BaseUrl ?? provider.BaseUrl,
            MaxTokens = MaxTokens ?? provider.MaxTokens,
            Seed = Seed ?? provider.Seed,
            StopSequences = StopSequences is { Count: > 0 } ? StopSequences : provider.StopSequences,
            SystemMessage = string.IsNullOrWhiteSpace(SystemMessage) ? provider.SystemMessage : SystemMessage,
            ApiVersion = string.IsNullOrWhiteSpace(ApiVersion) ? provider.ApiVersion : ApiVersion,
            WorkspaceId = string.IsNullOrWhiteSpace(WorkspaceId) ? provider.WorkspaceId : WorkspaceId,
            CustomParameters = MergeCustomParameters(provider.CustomParameters, CustomParameters),
            TimeoutSeconds = TimeoutSeconds ?? provider.TimeoutSeconds,
            Tools = Tools ?? provider.Tools,
            GrammarGbnf = string.IsNullOrWhiteSpace(GrammarGbnf) ? provider.GrammarGbnf : GrammarGbnf,
            Thinking = Thinking ?? provider.Thinking,
            Cache = Cache ?? provider.Cache,
            ResponseFormat = ResponseFormat ?? provider.ResponseFormat,
        };
    }

    /// <summary>The provider's custom parameters overlaid with the call's — the call's keys win.</summary>
    private static IReadOnlyDictionary<string, object> MergeCustomParameters(
        IReadOnlyDictionary<string, object>? inherited,
        IReadOnlyDictionary<string, object>? own)
    {
        if (inherited is not { Count: > 0 })
            return own ?? new Dictionary<string, object>();
        if (own is not { Count: > 0 })
            return inherited;

        var merged = new Dictionary<string, object>(inherited.Count + own.Count);
        foreach (var (key, value) in inherited)
            merged[key] = value;
        foreach (var (key, value) in own)
            merged[key] = value;
        return merged;
    }

    /// <summary>Creates a new <see cref="LlmConfig"/> with the specified model and optional API key.</summary>
    /// <param name="model">The model identifier (must not be null or empty).</param>
    /// <param name="apiKey">The API key (deprecated; prefer secret-based resolution).</param>
    /// <returns>A new <see cref="LlmConfig"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="model"/> is null or empty.</exception>
    public static LlmConfig Create(string model, string? apiKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new LlmConfig(model, apiKey);
    }

    /// <summary>
    /// Creates a validated <see cref="LlmConfig"/> with full parameter control.
    /// </summary>
    /// <remarks>All parameters have defaults; the high count reflects the LLM configuration surface area.</remarks>
#pragma warning disable S107 // Methods should not have too many parameters — LLM config requires all standard inference parameters
    public static LlmConfig CreateValidated(
        string model,
        double temperature = LlmDefaults.DefaultTemperature,
        int? maxTokens = null,
        double topP = 1.0,
        double frequencyPenalty = 0.0,
        double presencePenalty = 0.0,
        int? timeoutSeconds = null,
        int maxRetries = LlmDefaults.DefaultMaxRetries,
        string? apiKey = null,
        Uri? baseUrl = null)
#pragma warning restore S107
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (temperature < 0.0 || temperature > 2.0)
            throw new ArgumentOutOfRangeException(nameof(temperature),
                $"Temperature must be between 0.0 and 2.0, but was {temperature}.");
        if (maxTokens is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "MaxTokens must be positive when pinned; leave it null for the model's documented maximum.");
        if (topP < 0.0 || topP > 1.0)
            throw new ArgumentOutOfRangeException(nameof(topP),
                $"TopP must be between 0.0 and 1.0, but was {topP}.");
        if (frequencyPenalty < -2.0 || frequencyPenalty > 2.0)
            throw new ArgumentOutOfRangeException(nameof(frequencyPenalty),
                $"FrequencyPenalty must be between -2.0 and 2.0, but was {frequencyPenalty}.");
        if (presencePenalty < -2.0 || presencePenalty > 2.0)
            throw new ArgumentOutOfRangeException(nameof(presencePenalty),
                $"PresencePenalty must be between -2.0 and 2.0, but was {presencePenalty}.");
        if (timeoutSeconds is <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "TimeoutSeconds must be positive when pinned; leave it null for the provider's timeout.");
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);

        return new LlmConfig(model, apiKey)
        {
            Temperature = temperature,
            MaxTokens = maxTokens,
            TopP = topP,
            FrequencyPenalty = frequencyPenalty,
            PresencePenalty = presencePenalty,
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries,
            BaseUrl = baseUrl
        };
    }

    /// <summary>
    /// Creates a configuration that pins nothing: no model — the call runs on the model of the
    /// profile it is sent to — and default sampling. The one configuration that names no model:
    /// what a crew's <c>llm:</c> block, <c>AgentBuilder.Thinking()</c>, the agent loops and the
    /// planner start from, so a setting that only changes a temperature or a profile never pulls
    /// one vendor's model onto another vendor's endpoint (GAP-17, GAP-18).
    /// </summary>
    /// <param name="profile">The host profile to run on; null for the host's default.</param>
    /// <returns>A configuration on <paramref name="profile"/>'s own model.</returns>
    public static LlmConfig OnProfile(string? profile = null)
    {
        return new LlmConfig { Profile = string.IsNullOrWhiteSpace(profile) ? null : profile };
    }

    /// <summary>
    /// Creates a configuration on the platform's default OpenAI model
    /// (<see cref="LlmDefaults.DefaultModelName"/>).
    /// </summary>
    /// <param name="apiKey">Optional API key.</param>
    /// <returns>A <see cref="LlmConfig"/> on the default model.</returns>
    public static LlmConfig WithDefaultModel(string? apiKey = null)
    {
        return new LlmConfig(LlmDefaults.DefaultModelName, apiKey);
    }

    /// <summary>Creates a configuration on the platform's default OpenAI model.</summary>
    /// <param name="apiKey">Optional API key.</param>
    /// <returns>A <see cref="LlmConfig"/> on the default model.</returns>
    [Obsolete("Renamed to WithDefaultModel: this factory has always returned the platform default model, which is no longer gpt-4 (LLM-01). Pass \"gpt-4\" to Create if you really want that model.")]
    public static LlmConfig Gpt4(string? apiKey = null) => WithDefaultModel(apiKey);

    /// <summary>Creates a GPT-3.5-Turbo configuration.</summary>
    /// <param name="apiKey">Optional API key.</param>
    /// <returns>A GPT-3.5-Turbo <see cref="LlmConfig"/>.</returns>
    public static LlmConfig Gpt35Turbo(string? apiKey = null)
    {
        return new LlmConfig(LlmDefaults.LegacyModelName, apiKey);
    }

    /// <summary>Creates a Claude configuration.</summary>
    /// <param name="apiKey">Optional API key.</param>
    /// <returns>A Claude <see cref="LlmConfig"/>.</returns>
    public static LlmConfig Claude(string? apiKey = null)
    {
        return new LlmConfig("claude-3-opus-20240229", apiKey)
        {
            BaseUrl = new Uri(AnthropicBaseUrl)
        };
    }

    /// <summary>Creates an Ollama (local) configuration.</summary>
    /// <param name="model">The model name.</param>
    /// <returns>An Ollama <see cref="LlmConfig"/>.</returns>
    public static LlmConfig Ollama(string model = "llama2")
    {
        return new LlmConfig(model)
        {
            BaseUrl = new Uri(OllamaBaseUrl)
        };
    }

    /// <summary>
    /// Creates a config on the platform's default OpenAI model carrying a secret name.
    /// The framework does not resolve it -- see <see cref="ApiKeySecretName"/>.
    /// </summary>
    /// <param name="apiKeySecretName">The secret name to record.</param>
    /// <returns>A <see cref="LlmConfig"/> whose key the host must still supply.</returns>
    public static LlmConfig WithDefaultModelSecret(string apiKeySecretName = "OPENAI_API_KEY")
    {
        return new LlmConfig(LlmDefaults.DefaultModelName) { ApiKeySecretName = apiKeySecretName };
    }

    /// <summary>Creates a default-model config carrying a secret name the framework does not resolve.</summary>
    /// <param name="apiKeySecretName">The secret name to record.</param>
    /// <returns>A <see cref="LlmConfig"/> whose key the host must still supply.</returns>
    [Obsolete("Renamed to WithDefaultModelSecret: this factory has always returned the platform default model, which is no longer gpt-4 (LLM-01).")]
    public static LlmConfig Gpt4WithSecret(string apiKeySecretName = "OPENAI_API_KEY")
        => WithDefaultModelSecret(apiKeySecretName);

    /// <summary>Creates a GPT-3.5-Turbo config carrying a secret name the framework does not resolve.</summary>
    /// <param name="apiKeySecretName">The secret name to record.</param>
    /// <returns>A GPT-3.5-Turbo <see cref="LlmConfig"/> whose key the host must still supply.</returns>
    public static LlmConfig Gpt35TurboWithSecret(string apiKeySecretName = "OPENAI_API_KEY")
    {
        return new LlmConfig(LlmDefaults.LegacyModelName) { ApiKeySecretName = apiKeySecretName };
    }

    /// <summary>Creates a Claude config carrying a secret name the framework does not resolve.</summary>
    /// <param name="apiKeySecretName">The secret name to record.</param>
    /// <returns>A Claude <see cref="LlmConfig"/> whose key the host must still supply.</returns>
    public static LlmConfig ClaudeWithSecret(string apiKeySecretName = "ANTHROPIC_API_KEY")
    {
        return new LlmConfig("claude-3-opus-20240229")
        {
            BaseUrl = new Uri(AnthropicBaseUrl),
            ApiKeySecretName = apiKeySecretName
        };
    }
}
