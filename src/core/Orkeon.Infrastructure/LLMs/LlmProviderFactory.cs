using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Simplified factory for creating LLM providers.
/// Pure factory pattern - no business logic.
/// </summary>
public sealed class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly OpenAIToolCallingStrategy _openAiStrategy;
    private readonly AnthropicToolCallingStrategy _anthropicStrategy;

    /// <summary>Initializes a new instance of <see cref="LlmProviderFactory"/>.</summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public LlmProviderFactory(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;

        // Create tool calling strategies
        _openAiStrategy = new OpenAIToolCallingStrategy(
            loggerFactory.CreateLogger<OpenAIToolCallParser>());
        _anthropicStrategy = new AnthropicToolCallingStrategy();
    }

    /// <inheritdoc />
    public IBasicLlmProvider Create(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return InferProviderType(config) switch
        {
            "ollama" => CreateOllamaProvider(config),
            "openai" => CreateOpenAIProvider(config),
            "anthropic" => CreateAnthropicProvider(config),
            "azure" or "azure-openai" => CreateAzureOpenAIProvider(config),
            "groq" => CreateGroqProvider(config),
            "together" or "togetherai" => CreateTogetherAiProvider(config),
            "qwen" => CreateQwenProvider(config),
            "deepseek" => CreateDeepSeekProvider(config),
            "kimi" or "moonshot" => CreateKimiProvider(config),
            "mistral" => CreateMistralProvider(config),
            "huggingface" or "hf" => CreateHuggingFaceProvider(config),
            "zai" or "glm" or "zhipu" => CreateZaiProvider(config),
            _ => CreateOpenAIProvider(config) // Default to OpenAI
        };
    }


    /// <inheritdoc />
    public IBasicLlmProvider Create(string providerType, LlmConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerType);
        ArgumentNullException.ThrowIfNull(config);

#pragma warning disable CA1308 // lowercase is the required wire/switch-key form, not a comparison normalization
        return providerType.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "ollama" => CreateOllamaProvider(config),
            "openai" => CreateOpenAIProvider(config),
            "anthropic" => CreateAnthropicProvider(config),
            "azure" or "azure-openai" => CreateAzureOpenAIProvider(config),
            "groq" => CreateGroqProvider(config),
            "together" or "togetherai" => CreateTogetherAiProvider(config),
            "qwen" => CreateQwenProvider(config),
            "deepseek" => CreateDeepSeekProvider(config),
            "kimi" or "moonshot" => CreateKimiProvider(config),
            "mistral" => CreateMistralProvider(config),
            "huggingface" or "hf" => CreateHuggingFaceProvider(config),
            "zai" or "glm" or "zhipu" => CreateZaiProvider(config),
            _ => throw new NotSupportedException($"Provider type '{providerType}' is not supported.")
        };
    }

    /// <summary>
    /// Infers provider type from configuration.
    /// Pure logic - no business rules.
    /// </summary>
    private static string InferProviderType(LlmConfig config)
    {
        var fromBaseUrl = InferFromBaseUrl(config.BaseUrl?.ToString());
        if (fromBaseUrl != null)
            return fromBaseUrl;

        var fromModel = InferFromModel(config.Model);
        if (fromModel != null)
            return fromModel;

#pragma warning disable CS0618 // Type or member is obsolete
        var fromApiKey = InferFromApiKey(config.ApiKey);
#pragma warning restore CS0618
        if (fromApiKey != null)
            return fromApiKey;

        return "openai"; // Default
    }

    /// <summary>
    /// Infers the provider type from the base URL patterns.
    /// </summary>
    private static string? InferFromBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return null;

#pragma warning disable CA1308 // normalized lowercase URL reused as the comparison-key form for all subsequent ordinal host matches
        var url = baseUrl.ToLowerInvariant();
#pragma warning restore CA1308

        return InferFromKnownHostPatterns(url) ?? InferFromGenericPatterns(url);
    }

    private static string? InferFromKnownHostPatterns(string url)
    {
        // Docker Model Runner: must be checked BEFORE the generic localhost rule for Ollama
        if (url.Contains("/engines/", StringComparison.Ordinal)
            || url.Contains("model-runner.docker.internal", StringComparison.Ordinal))
            return "openai";
        if (url.Contains("azure", StringComparison.Ordinal) || url.Contains(".cognitiveservices.", StringComparison.Ordinal))
            return "azure-openai";
        if (url.Contains("groq.com", StringComparison.Ordinal))
            return "groq";
        if (url.Contains("together.xyz", StringComparison.Ordinal))
            return "together";
        // Qwen / Alibaba Model Studio: the mainland host (dashscope.aliyuncs.com), the
        // international host (dashscope-intl.aliyuncs.com — note it does NOT contain the
        // mainland string), and the per-workspace regional hosts
        // ({workspace}.{region}.maas.aliyuncs.com).
        if (url.Contains("dashscope.aliyuncs.com", StringComparison.Ordinal)
            || url.Contains("dashscope-intl.aliyuncs.com", StringComparison.Ordinal)
            || url.Contains("maas.aliyuncs.com", StringComparison.Ordinal))
            return "qwen";
        if (url.Contains("deepseek.com", StringComparison.Ordinal))
        {
            GuardAgainstDeepSeekAnthropicEndpoint(url);
            return "deepseek";
        }
        // Kimi: mainland (moonshot.cn) and international (moonshot.ai) hosts.
        if (url.Contains("moonshot.cn", StringComparison.Ordinal)
            || url.Contains("moonshot.ai", StringComparison.Ordinal))
            return "kimi";
        if (url.Contains("mistral.ai", StringComparison.Ordinal))
            return "mistral";
        if (url.Contains("huggingface.co", StringComparison.Ordinal) || url.Contains("hf.co", StringComparison.Ordinal))
            return "huggingface";
        // Z.AI (Zhipu GLM): match the full host, not the bare "z.ai" substring
        // (which would also hit any *z.ai domain), plus the mainland bigmodel.cn twin.
        if (url.Contains("api.z.ai", StringComparison.Ordinal) || url.Contains("bigmodel.cn", StringComparison.Ordinal))
            return "zai";
        return null;
    }

    private static string? InferFromGenericPatterns(string url)
    {
        if (url.Contains("openai", StringComparison.Ordinal))
            return "openai";
        if (url.Contains("anthropic", StringComparison.Ordinal))
            return "anthropic";
        if (url.Contains("localhost", StringComparison.Ordinal) || url.Contains("11434", StringComparison.Ordinal))
            return "ollama";
        return null;
    }

    /// <summary>
    /// Infers the provider type from the model name patterns.
    /// </summary>
    private static string? InferFromModel(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return null;

#pragma warning disable CA1308 // normalized lowercase model name reused as the comparison-key form for all subsequent ordinal prefix matches
        var m = model.ToLowerInvariant();
#pragma warning restore CA1308

        if (m.StartsWith("gpt", StringComparison.Ordinal))
            return "openai";
        if (m.StartsWith("claude", StringComparison.Ordinal))
            return "anthropic";
        if (m.StartsWith("llama", StringComparison.Ordinal) || m.StartsWith("codellama", StringComparison.Ordinal))
            return "ollama";
        if (IsOllamaMistralTag(m))
            return "ollama";
        if (m.StartsWith("mistral", StringComparison.Ordinal) || m.StartsWith("ministral", StringComparison.Ordinal))
            return "mistral";
        if (m.Contains("mixtral", StringComparison.Ordinal) || m.Contains("groq", StringComparison.Ordinal))
            return "groq";
        if (m.StartsWith("qwen", StringComparison.Ordinal))
            return "qwen";
        if (m.StartsWith("deepseek", StringComparison.Ordinal))
            return "deepseek";
        if (m.StartsWith("moonshot", StringComparison.Ordinal))
            return "kimi";
        if (m.StartsWith("glm", StringComparison.Ordinal))
            return "zai";

        return null;
    }

    /// <summary>
    /// Rejects <c>api.deepseek.com/anthropic</c>, which speaks the Anthropic Messages API and
    /// not the OpenAI dialect the DeepSeek provider builds (G-22).
    /// </summary>
    /// <remarks>
    /// Host-based inference matches <c>deepseek.com</c> and would happily route this endpoint
    /// to the OpenAI-compatible provider, producing malformed requests whose error surfaces
    /// far from its cause. Failing here names the cause instead. Orkeon has no reason to use
    /// this alternate path — the standard DeepSeek endpoint exposes the same models.
    /// </remarks>
    private static void GuardAgainstDeepSeekAnthropicEndpoint(string normalizedUrl)
    {
        if (!normalizedUrl.Contains("/anthropic", StringComparison.Ordinal))
            return;

        throw new NotSupportedException(
            "The DeepSeek base URL points at the /anthropic path, which speaks the Anthropic " +
            "Messages API dialect, not the OpenAI dialect this provider builds. Use the " +
            "standard endpoint (https://api.deepseek.com) — it serves the same models.");
    }

    /// <summary>
    /// True for the two model-name shapes that unambiguously designate the <em>local</em>
    /// Mistral pulled by Ollama: the bare name <c>mistral</c> and a tagged
    /// <c>mistral:&lt;tag&gt;</c> (e.g. <c>mistral:7b</c>).
    /// </summary>
    /// <remarks>
    /// LLM-01 / G-14: the previous rule routed <em>every</em> <c>mistral*</c> model to Ollama,
    /// so a cloud identifier such as <c>mistral-medium-3-5-26-04</c> was sent to
    /// <c>localhost:11434</c>. Versioned identifiers now reach the Mistral cloud instead. The
    /// arbitration of MISTRAL-PROVIDER-PLAN §0 is preserved for the bare name. Ollama users who
    /// run a hyphenated local tag (<c>mistral-nemo</c>, <c>mistral-small3.2</c>) set
    /// <see cref="LlmConfig.BaseUrl"/> to their server — base-URL inference runs first and wins.
    /// </remarks>
    private static bool IsOllamaMistralTag(string normalizedModel) =>
        string.Equals(normalizedModel, "mistral", StringComparison.Ordinal)
        || normalizedModel.StartsWith("mistral:", StringComparison.Ordinal);

    /// <summary>
    /// Infers the provider type from the API key prefix.
    /// </summary>
    private static string? InferFromApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return null;

        if (apiKey.StartsWith("hf_", StringComparison.Ordinal))
            return "huggingface";

        return null;
    }

    /// <summary>
    /// Wraps a freshly built provider in an <see cref="LlmProviderAdapter"/>.
    /// </summary>
    /// <remarks>
    /// R10.2/ANT-006: the provider is a disposable, but ownership transfers to the returned
    /// adapter (and through it to the caller) — it is not lost at scope exit. The provider's
    /// <c>Dispose</c> is a documented no-op anyway (its <see cref="HttpClient"/> is owned by
    /// <see cref="IHttpClientFactory"/>), so suppressing CA2000 here is correct, not a cover-up.
    /// Each per-provider builder lambda returns its disposable, so the construction sites carry
    /// no CA2000 warning of their own.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the provider transfers to the returned LlmProviderAdapter; the provider's Dispose is a no-op (its HttpClient is IHttpClientFactory-managed).")]
    private LlmProviderAdapter Adapt<TProvider>(Func<ILogger<TProvider>, ILlmProvider> build)
    {
        var provider = build(_loggerFactory.CreateLogger<TProvider>());
        return new LlmProviderAdapter(provider);
    }

    /// <summary>
    /// Creates Ollama provider instance.
    /// R10.7: intentionally built WITHOUT a native tool calling strategy. The provider targets
    /// Ollama's prompt-completion API (/api/generate); Ollama's native tools live on /api/chat
    /// with a response shape (message.tool_calls, arguments as JSON object) that the OpenAI
    /// parser does not read. Tool calling goes through the text fallback — see
    /// docs/reference/limitations.md.
    /// </summary>
    private LlmProviderAdapter CreateOllamaProvider(LlmConfig config)
        => Adapt<OllamaLlmProvider>(logger => new OllamaLlmProvider(config, _httpClientFactory, logger));

    /// <summary>Creates OpenAI provider instance.</summary>
    private LlmProviderAdapter CreateOpenAIProvider(LlmConfig config)
        => Adapt<OpenAIProvider>(logger => new OpenAIProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Anthropic provider instance.</summary>
    private LlmProviderAdapter CreateAnthropicProvider(LlmConfig config)
        => Adapt<AnthropicLlmProvider>(logger => new AnthropicLlmProvider(config, _httpClientFactory, _anthropicStrategy, logger));

    /// <summary>
    /// Creates Azure OpenAI provider instance.
    /// R10.7: Azure OpenAI speaks the OpenAI dialect — wire the native tool calling strategy
    /// like the other OpenAI-compatible providers.
    /// </summary>
    private LlmProviderAdapter CreateAzureOpenAIProvider(LlmConfig config)
        => Adapt<AzureOpenAILlmProvider>(logger => new AzureOpenAILlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Groq provider instance.</summary>
    private LlmProviderAdapter CreateGroqProvider(LlmConfig config)
        => Adapt<GroqLlmProvider>(logger => new GroqLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Together AI provider instance.</summary>
    private LlmProviderAdapter CreateTogetherAiProvider(LlmConfig config)
        => Adapt<TogetherAiLlmProvider>(logger => new TogetherAiLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Qwen (Alibaba DashScope) provider instance.</summary>
    private LlmProviderAdapter CreateQwenProvider(LlmConfig config)
        => Adapt<QwenLlmProvider>(logger => new QwenLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates DeepSeek provider instance.</summary>
    private LlmProviderAdapter CreateDeepSeekProvider(LlmConfig config)
        => Adapt<DeepSeekLlmProvider>(logger => new DeepSeekLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Kimi (Moonshot AI) provider instance.</summary>
    private LlmProviderAdapter CreateKimiProvider(LlmConfig config)
        => Adapt<KimiLlmProvider>(logger => new KimiLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Mistral AI provider instance.</summary>
    private LlmProviderAdapter CreateMistralProvider(LlmConfig config)
        => Adapt<MistralLlmProvider>(logger => new MistralLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates HuggingFace provider instance.</summary>
    private LlmProviderAdapter CreateHuggingFaceProvider(LlmConfig config)
        => Adapt<HuggingFaceLlmProvider>(logger => new HuggingFaceLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Z.AI (Zhipu GLM) provider instance.</summary>
    private LlmProviderAdapter CreateZaiProvider(LlmConfig config)
        => Adapt<ZaiLlmProvider>(logger => new ZaiLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));
}
