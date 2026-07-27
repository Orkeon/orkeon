using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Configuration for Language Model settings.</summary>
public sealed record LlmConfig
{
    /// <summary>Default base URL for the Anthropic (Claude) API.</summary>
    private const string AnthropicBaseUrl = "https://api.anthropic.com";

    /// <summary>Default base URL for a locally running Ollama server.</summary>
    private const string OllamaBaseUrl = "http://localhost:11434";

    /// <summary>Gets the model identifier (e.g., "gpt-4").</summary>
    public string Model { get; init; } = LlmDefaults.DefaultModelName;

    /// <summary>
    /// Direct API key. Prefer <see cref="ApiKeySecretName"/> with ISecretProvider instead
    /// to avoid storing secrets in configuration objects.
    /// </summary>
    [Obsolete("Use ApiKeySecretName with ISecretProvider instead. Will be removed in v2.0.")]
    public string? ApiKey { get; init; }

    /// <summary>
    /// Logical name of the secret to resolve via ISecretProvider at runtime.
    /// When set, this takes precedence over <see cref="ApiKey"/>.
    /// </summary>
    public string? ApiKeySecretName { get; init; }

    /// <summary>Gets the base URL for the LLM provider.</summary>
    public Uri? BaseUrl { get; init; }
    /// <summary>Gets the sampling temperature (0.0 to 1.0).</summary>
    public double Temperature { get; init; } = LlmDefaults.DefaultTemperature;
    /// <summary>Gets the maximum number of tokens to generate.</summary>
    public int MaxTokens { get; init; } = LlmDefaults.DefaultContextWindowTokens;
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
    /// Additional provider-specific parameters.
    /// Prefer the typed properties (<see cref="SystemMessage"/>, <see cref="ApiVersion"/>)
    /// for well-known keys. Use this dictionary only for provider-specific extensions.
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomParameters { get; init; } = new Dictionary<string, object>();
    /// <summary>Gets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
    /// <summary>Gets the maximum number of retries on transient failures.</summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;

    /// <summary>Gets the tool schemas to include in the LLM request payload.</summary>
    public IReadOnlyList<ToolSchema>? Tools { get; init; }

    /// <summary>Gets the tool calling mode (Auto, Required, None).</summary>
    public ToolCallMode ToolMode { get; init; } = ToolCallMode.Auto;

    /// <summary>
    /// Optional GBNF grammar string to constrain generation. When set and the provider supports it
    /// (llama.cpp via Ollama), the grammar is forwarded in the HTTP payload (<c>grammar</c> field
    /// on <c>/api/generate</c>). Used by StructuredOutputResolver to guarantee schema-valid JSON.
    /// </summary>
    public string? GrammarGbnf { get; init; }

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
    /// implement the <c>response_format</c> field (DeepSeek today; OpenAI/Groq can opt in
    /// later). <c>null</c> = provider default (free text).
    /// </summary>
    public LlmResponseFormat? ResponseFormat { get; init; }

    /// <summary>Initializes a new instance of <see cref="LlmConfig"/> with default settings.</summary>
    private LlmConfig() { }

    /// <summary>Initializes a new instance of <see cref="LlmConfig"/> with a model and optional API key.</summary>
    /// <param name="model">The model identifier.</param>
    /// <param name="apiKey">The API key (deprecated; prefer secret-based resolution).</param>
    private LlmConfig(string model, string? apiKey = null)
    {
        Model = model;
#pragma warning disable CS0618 // Type or member is obsolete
        ApiKey = apiKey;
#pragma warning restore CS0618
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
        int maxTokens = LlmDefaults.DefaultContextWindowTokens,
        double topP = 1.0,
        double frequencyPenalty = 0.0,
        double presencePenalty = 0.0,
        int timeoutSeconds = 30,
        int maxRetries = AgentDefaults.MaxRetryLimit,
        string? apiKey = null,
        Uri? baseUrl = null)
#pragma warning restore S107
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (temperature < 0.0 || temperature > 2.0)
            throw new ArgumentOutOfRangeException(nameof(temperature),
                $"Temperature must be between 0.0 and 2.0, but was {temperature}.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        if (topP < 0.0 || topP > 1.0)
            throw new ArgumentOutOfRangeException(nameof(topP),
                $"TopP must be between 0.0 and 1.0, but was {topP}.");
        if (frequencyPenalty < -2.0 || frequencyPenalty > 2.0)
            throw new ArgumentOutOfRangeException(nameof(frequencyPenalty),
                $"FrequencyPenalty must be between -2.0 and 2.0, but was {frequencyPenalty}.");
        if (presencePenalty < -2.0 || presencePenalty > 2.0)
            throw new ArgumentOutOfRangeException(nameof(presencePenalty),
                $"PresencePenalty must be between -2.0 and 2.0, but was {presencePenalty}.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutSeconds);
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

    /// <summary>Creates a default LLM configuration.</summary>
    /// <returns>A default <see cref="LlmConfig"/>.</returns>
    public static LlmConfig Default()
    {
        return new LlmConfig();
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
    /// Creates a config on the platform's default OpenAI model that resolves the API key from
    /// ISecretProvider at runtime.
    /// </summary>
    /// <param name="apiKeySecretName">The secret name to resolve.</param>
    /// <returns>A <see cref="LlmConfig"/> using secret-based API key resolution.</returns>
    public static LlmConfig WithDefaultModelSecret(string apiKeySecretName = "OPENAI_API_KEY")
    {
        return new LlmConfig(LlmDefaults.DefaultModelName) { ApiKeySecretName = apiKeySecretName };
    }

    /// <summary>Creates a default-model config that resolves the API key from ISecretProvider at runtime.</summary>
    /// <param name="apiKeySecretName">The secret name to resolve.</param>
    /// <returns>A <see cref="LlmConfig"/> using secret-based API key resolution.</returns>
    [Obsolete("Renamed to WithDefaultModelSecret: this factory has always returned the platform default model, which is no longer gpt-4 (LLM-01).")]
    public static LlmConfig Gpt4WithSecret(string apiKeySecretName = "OPENAI_API_KEY")
        => WithDefaultModelSecret(apiKeySecretName);

    /// <summary>Creates a GPT-3.5-Turbo config that resolves the API key from ISecretProvider at runtime.</summary>
    /// <param name="apiKeySecretName">The secret name to resolve.</param>
    /// <returns>A GPT-3.5-Turbo <see cref="LlmConfig"/> using secret-based API key resolution.</returns>
    public static LlmConfig Gpt35TurboWithSecret(string apiKeySecretName = "OPENAI_API_KEY")
    {
        return new LlmConfig(LlmDefaults.LegacyModelName) { ApiKeySecretName = apiKeySecretName };
    }

    /// <summary>Creates a Claude config that resolves the API key from ISecretProvider at runtime.</summary>
    /// <param name="apiKeySecretName">The secret name to resolve.</param>
    /// <returns>A Claude <see cref="LlmConfig"/> using secret-based API key resolution.</returns>
    public static LlmConfig ClaudeWithSecret(string apiKeySecretName = "ANTHROPIC_API_KEY")
    {
        return new LlmConfig("claude-3-opus-20240229")
        {
            BaseUrl = new Uri(AnthropicBaseUrl),
            ApiKeySecretName = apiKeySecretName
        };
    }
}
