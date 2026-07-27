using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.LLMs.Base;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// OpenAI provider implementation using the OpenAI-compatible base.
/// </summary>
public class OpenAIProvider : OpenAICompatibleProviderBase
{
    /// <inheritdoc />
    public override string Name => "OpenAI";

    /// <inheritdoc />
    protected override Uri DefaultBaseUrl => new(LlmEndpoints.OpenAI);

    /// <inheritdoc />
    protected override string DefaultModel => ProviderDefaults.OpenAIDefaults.DefaultModel;

    /// <inheritdoc />
    protected override string ProviderDisplayName => "OpenAI";

    /// <summary>
    /// OpenAI offers Structured Outputs (server-validated JSON Schema), a reasoning effort
    /// hint that cannot be switched off, automatic prompt caching (nothing to declare on the
    /// wire), and vision: messages carrying
    /// <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmMessage.MultiModalContent"/> with
    /// image parts are sent as structured <c>text</c> + <c>image_url</c> content parts
    /// (http(s) URL or base64 <c>data:</c> URL).
    /// </summary>
    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        ResponseFormat = ResponseFormatSupport.JsonSchema,
        Thinking = ThinkingSupport.EffortOnly,
        Vision = true,
    };

    /// <summary>Initializes a new instance of <see cref="OpenAIProvider"/>.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">Optional logger.</param>
    public OpenAIProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAIProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    /// <summary>Constructor overload that accepts an optional resilience policy for testing.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="resiliencePolicy">Optional custom resilience policy.</param>
    /// <param name="logger">Optional logger.</param>
    public OpenAIProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<OpenAIProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }

    /// <summary>Constructor overload that accepts a tool calling strategy.</summary>
    /// <param name="config">The LLM configuration.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="toolCallingStrategy">Optional tool calling strategy for native tool support.</param>
    /// <param name="logger">Optional logger.</param>
    public OpenAIProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IToolCallingStrategy? toolCallingStrategy,
        ILogger<OpenAIProvider>? logger = null)
        : base(config, httpClientFactory, toolCallingStrategy, logger)
    {
    }
}
