using Orkeon.Constants.Configuration;
using Orkeon.Constants.Llm;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.LLMs.CallMode;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Simplified factory for creating LLM providers.
/// Pure factory pattern - no business logic.
/// </summary>
/// <remarks>
/// The only place a vendor provider is built, and therefore the place it is metered and limited:
/// every provider comes out wrapped in <see cref="MeteredLlmProvider"/> when the host registered an
/// <see cref="ILlmUsageSink"/> (STUDIO-42), and in the host's <see cref="RateLimitedLlmProvider"/>
/// when it registered an <see cref="ILlmRateLimiter"/> (GAP-38). A provider built anywhere else
/// would spend tokens no meter sees, past every cap — an architecture test holds that line.
/// </remarks>
public sealed class LlmProviderFactory : ILlmProviderFactory
{
    private const string LlmBaseUrlSetting = "Llm:BaseUrl";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILlmUsageSink? _usageSink;
    private readonly ILlmRateLimiter? _rateLimiter;
    private readonly ILlmCallObserver _callObserver;
    private readonly ITokenCounter _tokenCounter;
    private readonly OpenAIToolCallingStrategy _openAiStrategy;
    private readonly AnthropicToolCallingStrategy _anthropicStrategy;

    /// <summary>Initializes a new instance of <see cref="LlmProviderFactory"/>.</summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="usageSink">
    /// The host's usage receiver: every provider built is metered for it. Null — no host
    /// listening — builds the providers unmetered.
    /// </param>
    /// <param name="rateLimiter">
    /// The host's limiter (<c>RateLimiting</c>): every call of a provider built takes one of its
    /// leases. Null builds the providers unlimited.
    /// </param>
    /// <param name="callObserver">The host's call observer: every call of a provider built is announced to it. Null observes nothing.</param>
    /// <param name="tokenCounter">Estimates the tokens the observer is told; null uses <see cref="SimpleTokenCounter"/>.</param>
    public LlmProviderFactory(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ILlmUsageSink? usageSink = null,
        ILlmRateLimiter? rateLimiter = null,
        ILlmCallObserver? callObserver = null,
        ITokenCounter? tokenCounter = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
        _usageSink = usageSink;
        _rateLimiter = rateLimiter;
        _callObserver = callObserver ?? NullLlmCallObserver.Instance;
        _tokenCounter = tokenCounter ?? new SimpleTokenCounter();

        // Create tool calling strategies
        _openAiStrategy = new OpenAIToolCallingStrategy(
            loggerFactory.CreateLogger<OpenAIToolCallParser>());
        _anthropicStrategy = new AnthropicToolCallingStrategy();
    }

    /// <summary>
    /// <paramref name="provider"/> announced on <paramref name="observer"/>; a provider already under
    /// a call-mode decorator (so already notifying its host's observer) is returned unchanged.
    /// </summary>
    public static ILlmProvider Observe(ILlmProvider provider, ILlmCallObserver observer, ITokenCounter? counter = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(observer);
        return CallModeLlmProvider.Of(provider) is not null
            ? provider
            : new CallModeLlmProvider(provider, LlmCallMode.Auto, NullLogger.Instance, observer, counter ?? new SimpleTokenCounter());
    }

    /// <inheritdoc />
    public IBasicLlmProvider Create(LlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return InferProviderType(config) switch
        {
            LlmProviderKeys.Ollama => CreateOllamaProvider(config),
            LlmProviderKeys.OpenAI => CreateOpenAIProvider(config),
            LlmProviderKeys.Anthropic => CreateAnthropicProvider(config),
            LlmProviderKeys.AzureShort or LlmProviderKeys.AzureOpenAI => CreateAzureOpenAIProvider(config),
            LlmProviderKeys.Together or LlmProviderKeys.TogetherAiAlias => CreateTogetherAiProvider(config),
            LlmProviderKeys.Qwen => CreateQwenProvider(config),
            LlmProviderKeys.DeepSeek => CreateDeepSeekProvider(config),
            LlmProviderKeys.Kimi or LlmProviderKeys.MoonshotAlias => CreateKimiProvider(config),
            LlmProviderKeys.Mistral => CreateMistralProvider(config),
            LlmProviderKeys.HuggingFace or LlmProviderKeys.HuggingFaceAlias => CreateHuggingFaceProvider(config),
            LlmProviderKeys.Gemini or LlmProviderKeys.GoogleAlias => CreateGeminiProvider(config),
            LlmProviderKeys.Grok or LlmProviderKeys.XaiAlias => CreateGrokProvider(config),
            LlmProviderKeys.MiniMax => CreateMiniMaxProvider(config),
            LlmProviderKeys.Zai or LlmProviderKeys.GlmAlias or LlmProviderKeys.ZhipuAlias => CreateZaiProvider(config),
            LlmProviderKeys.OpenRouter => CreateOpenRouterProvider(config),
            LlmProviderKeys.Mammouth => CreateMammouthProvider(config),
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
            LlmProviderKeys.Ollama => CreateOllamaProvider(config),
            LlmProviderKeys.OpenAI => CreateOpenAIProvider(config),
            LlmProviderKeys.Anthropic => CreateAnthropicProvider(config),
            LlmProviderKeys.AzureShort or LlmProviderKeys.AzureOpenAI => CreateAzureOpenAIProvider(config),
            LlmProviderKeys.Together or LlmProviderKeys.TogetherAiAlias => CreateTogetherAiProvider(config),
            LlmProviderKeys.Qwen => CreateQwenProvider(config),
            LlmProviderKeys.DeepSeek => CreateDeepSeekProvider(config),
            LlmProviderKeys.Kimi or LlmProviderKeys.MoonshotAlias => CreateKimiProvider(config),
            LlmProviderKeys.Mistral => CreateMistralProvider(config),
            LlmProviderKeys.HuggingFace or LlmProviderKeys.HuggingFaceAlias => CreateHuggingFaceProvider(config),
            LlmProviderKeys.Gemini or LlmProviderKeys.GoogleAlias => CreateGeminiProvider(config),
            LlmProviderKeys.Grok or LlmProviderKeys.XaiAlias => CreateGrokProvider(config),
            LlmProviderKeys.MiniMax => CreateMiniMaxProvider(config),
            LlmProviderKeys.Zai or LlmProviderKeys.GlmAlias or LlmProviderKeys.ZhipuAlias => CreateZaiProvider(config),
            LlmProviderKeys.OpenRouter => CreateOpenRouterProvider(config),
            LlmProviderKeys.Mammouth => CreateMammouthProvider(config),
            LlmProviderKeys.Custom or LlmProviderKeys.DockerModelRunner => CreateOpenAIProvider(config),
            _ => throw new NotSupportedException($"Provider type '{providerType}' is not supported.")
        };
    }

    /// <summary>
    /// The provider key the Domain reading names for the configuration; a refused address throws.
    /// </summary>
    private static string InferProviderType(LlmConfig config)
    {
        var baseUrl = config.BaseUrl?.ToString();
#pragma warning disable CS0618 // Type or member is obsolete
        var reading = LlmEndpointReading.Of(baseUrl, config.Model, config.ApiKey);
#pragma warning restore CS0618
        if (reading.Notice == LlmEndpointNotice.Refused)
            throw new NotSupportedException(RefusalMessage(config.BaseUrl?.OriginalString, reading.ProviderKey));

        return reading.ProviderKey;
    }

    /// <summary>
    /// Names the refusal the reading decided: a refused address read as a vendor's is that vendor's
    /// <c>/anthropic</c> path; any other is unreadable. Neither message carries user info or query.
    /// </summary>
    private static string RefusalMessage(string? baseUrl, string providerKey) =>
        providerKey == LlmProviderKeys.Custom
            ? OperatorMessages.LlmBaseUrlRefused(LlmBaseUrlSetting, baseUrl ?? string.Empty)
            : OperatorMessages.LlmVendorAnthropicPathRefused(LlmBaseUrlSetting, baseUrl ?? string.Empty, providerKey);

    /// <summary>
    /// Meters and limits a freshly built provider (<see cref="LlmProviderEntrance"/>) and wraps it in
    /// an <see cref="LlmProviderAdapter"/>, whose <see cref="LlmProviderAdapter.UnderlyingProvider"/>
    /// is then the entered one: every consumer that unwraps the adapter — the chat client, the
    /// native tool-calling loop, the scripting facade — calls through the meter and the limiter.
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
    private LlmProviderAdapter Adapt<TProvider>(LlmConfig config, Func<ILogger<TProvider>, ILlmProvider> build)
    {
        var logger = _loggerFactory.CreateLogger<TProvider>();
        var provider = new CallModeLlmProvider(build(logger), config.CallMode, logger, _callObserver, _tokenCounter);
        return new LlmProviderAdapter(LlmProviderEntrance.Enter(provider, _usageSink, _rateLimiter));
    }

    /// <summary>
    /// Creates Ollama provider instance, wired for native tool calling (LLM-07 / G-10).
    /// </summary>
    /// <remarks>
    /// The provider now reaches <c>/api/chat</c> whenever tools or images are in play, and
    /// reshapes its response into the OpenAI body the parser reads — so the OpenAI strategy
    /// applies. It keeps the prompt-completion path (and therefore the text fallback) for
    /// plain conversations and for models without tool support.
    /// </remarks>
    private LlmProviderAdapter CreateOllamaProvider(LlmConfig config)
        => Adapt<OllamaLlmProvider>(config, logger => new OllamaLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates OpenAI provider instance.</summary>
    private LlmProviderAdapter CreateOpenAIProvider(LlmConfig config)
        => Adapt<OpenAIProvider>(config, logger => new OpenAIProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Anthropic provider instance.</summary>
    private LlmProviderAdapter CreateAnthropicProvider(LlmConfig config)
        => Adapt<AnthropicLlmProvider>(config, logger => new AnthropicLlmProvider(config, _httpClientFactory, _anthropicStrategy, logger));

    /// <summary>
    /// Creates Azure OpenAI provider instance.
    /// R10.7: Azure OpenAI speaks the OpenAI dialect — wire the native tool calling strategy
    /// like the other OpenAI-compatible providers.
    /// </summary>
    private LlmProviderAdapter CreateAzureOpenAIProvider(LlmConfig config)
        => Adapt<AzureOpenAILlmProvider>(config, logger => new AzureOpenAILlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Together AI provider instance.</summary>
    private LlmProviderAdapter CreateTogetherAiProvider(LlmConfig config)
        => Adapt<TogetherAiLlmProvider>(config, logger => new TogetherAiLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Qwen (Alibaba DashScope) provider instance.</summary>
    private LlmProviderAdapter CreateQwenProvider(LlmConfig config)
        => Adapt<QwenLlmProvider>(config, logger => new QwenLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates DeepSeek provider instance.</summary>
    private LlmProviderAdapter CreateDeepSeekProvider(LlmConfig config)
        => Adapt<DeepSeekLlmProvider>(config, logger => new DeepSeekLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Kimi (Moonshot AI) provider instance.</summary>
    private LlmProviderAdapter CreateKimiProvider(LlmConfig config)
        => Adapt<KimiLlmProvider>(config, logger => new KimiLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates MiniMax provider instance.</summary>
    private LlmProviderAdapter CreateMiniMaxProvider(LlmConfig config)
        => Adapt<MiniMaxLlmProvider>(config, logger => new MiniMaxLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates x.AI (Grok) provider instance.</summary>
    private LlmProviderAdapter CreateGrokProvider(LlmConfig config)
        => Adapt<GrokLlmProvider>(config, logger => new GrokLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Google Gemini provider instance.</summary>
    private LlmProviderAdapter CreateGeminiProvider(LlmConfig config)
        => Adapt<GeminiLlmProvider>(config, logger => new GeminiLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Mistral AI provider instance.</summary>
    private LlmProviderAdapter CreateMistralProvider(LlmConfig config)
        => Adapt<MistralLlmProvider>(config, logger => new MistralLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates HuggingFace provider instance.</summary>
    private LlmProviderAdapter CreateHuggingFaceProvider(LlmConfig config)
        => Adapt<HuggingFaceLlmProvider>(config, logger => new HuggingFaceLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Z.AI (Zhipu GLM) provider instance.</summary>
    private LlmProviderAdapter CreateZaiProvider(LlmConfig config)
        => Adapt<ZaiLlmProvider>(config, logger => new ZaiLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates OpenRouter provider instance.</summary>
    private LlmProviderAdapter CreateOpenRouterProvider(LlmConfig config)
        => Adapt<OpenRouterLlmProvider>(config, logger => new OpenRouterLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));

    /// <summary>Creates Mammouth AI provider instance.</summary>
    private LlmProviderAdapter CreateMammouthProvider(LlmConfig config)
        => Adapt<MammouthLlmProvider>(config, logger => new MammouthLlmProvider(config, _httpClientFactory, _openAiStrategy, logger));
}
